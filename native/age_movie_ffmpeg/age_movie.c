#include "age_movie.h"

#include <errno.h>
#include <limits.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

#include <libavcodec/avcodec.h>
#include <libavformat/avformat.h>
#include <libavutil/avutil.h>
#include <libavutil/error.h>
#include <libavutil/imgutils.h>
#include <libavutil/mathematics.h>
#include <libavutil/channel_layout.h>
#include <libswresample/swresample.h>
#include <libswscale/swscale.h>

#define AGE_MOVIE_ABI_VERSION 3u
#define AGE_MOVIE_IO_BUFFER_SIZE 32768
#define AGE_MOVIE_ERROR_SIZE 512

typedef struct age_movie_io_state {
    const uint8_t *payload;
    size_t payload_size;
    size_t position;
} age_movie_io_state;

struct age_movie {
    uint8_t *payload;
    size_t payload_size;
    age_movie_io_state video_io_state;
    AVIOContext *video_io;
    AVFormatContext *video_format;
    AVCodecContext *video_codec;
    AVPacket *video_packet;
    AVFrame *video_frame;
    AVFrame *rgba_frame;
    struct SwsContext *sws;
    int video_stream_index;
    AVRational video_time_base;
    int video_input_eof;
    int video_decoder_draining;
    int video_decoder_eof;
    int video_seek_preroll;
    int64_t decoded_video_frame_count;
    int64_t video_fallback_origin_ms;

    age_movie_io_state audio_io_state;
    AVIOContext *audio_io;
    AVFormatContext *audio_format;
    AVCodecContext *audio_codec;
    AVPacket *audio_packet;
    AVFrame *audio_frame;
    SwrContext *swr;
    int audio_stream_index;
    AVRational audio_time_base;
    int audio_input_eof;
    int audio_decoder_draining;
    int audio_decoder_eof;
    int audio_frame_pending;
    int audio_seek_preroll;
    int64_t decoded_audio_sample_count;
    int64_t audio_fallback_origin_ms;
    int audio_sample_rate;

    int64_t timeline_origin_ms;
    int64_t stop_time_ms;
    int width;
    int height;
    char error[AGE_MOVIE_ERROR_SIZE];
};

static void copy_error(char *destination, size_t destination_size, const char *message) {
    if (destination == NULL || destination_size == 0) {
        return;
    }
    snprintf(destination, destination_size, "%s", message != NULL ? message : "unknown FFmpeg error");
}

static void set_error(age_movie *movie, const char *operation, int ffmpeg_error) {
    char detail[AV_ERROR_MAX_STRING_SIZE] = {0};
    if (av_strerror(ffmpeg_error, detail, sizeof(detail)) < 0) {
        snprintf(detail, sizeof(detail), "FFmpeg error %d", ffmpeg_error);
    }
    snprintf(movie->error, sizeof(movie->error), "%s: %s", operation, detail);
}

static int read_packet(void *opaque, uint8_t *buffer, int buffer_size) {
    age_movie_io_state *state = (age_movie_io_state *)opaque;
    if (state == NULL || buffer == NULL || buffer_size <= 0) {
        return AVERROR(EINVAL);
    }
    if (state->position >= state->payload_size) {
        return AVERROR_EOF;
    }
    size_t remaining = state->payload_size - state->position;
    size_t requested = (size_t)buffer_size;
    size_t count = remaining < requested ? remaining : requested;
    memcpy(buffer, state->payload + state->position, count);
    state->position += count;
    return (int)count;
}

static int64_t seek_payload(void *opaque, int64_t offset, int whence) {
    age_movie_io_state *state = (age_movie_io_state *)opaque;
    if (state == NULL) {
        return AVERROR(EINVAL);
    }
    if ((whence & AVSEEK_SIZE) != 0) {
        return state->payload_size <= INT64_MAX ? (int64_t)state->payload_size : AVERROR(EOVERFLOW);
    }

    int base_whence = whence & ~AVSEEK_FORCE;
    int64_t base;
    switch (base_whence) {
        case SEEK_SET: base = 0; break;
        case SEEK_CUR:
            if (state->position > INT64_MAX) return AVERROR(EOVERFLOW);
            base = (int64_t)state->position;
            break;
        case SEEK_END:
            if (state->payload_size > INT64_MAX) return AVERROR(EOVERFLOW);
            base = (int64_t)state->payload_size;
            break;
        default: return AVERROR(EINVAL);
    }
    if (offset == INT64_MIN || (offset > 0 && base > INT64_MAX - offset) ||
        (offset < 0 && base < -offset)) {
        return AVERROR(EOVERFLOW);
    }
    int64_t target = base + offset;
    if (target < 0 || (uint64_t)target > state->payload_size) {
        return AVERROR(EINVAL);
    }
    state->position = (size_t)target;
    return target;
}

static int64_t rescale_ms_down(int64_t value, AVRational time_base) {
    return av_rescale_q_rnd(value, time_base, (AVRational){1, 1000},
        AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
}

static int open_format_context(age_movie *movie, age_movie_io_state *state,
    AVIOContext **out_io, AVFormatContext **out_format) {
    state->payload = movie->payload;
    state->payload_size = movie->payload_size;
    state->position = 0;
    uint8_t *io_buffer = (uint8_t *)av_malloc(AGE_MOVIE_IO_BUFFER_SIZE);
    if (io_buffer == NULL) return AVERROR(ENOMEM);
    AVIOContext *io = avio_alloc_context(io_buffer, AGE_MOVIE_IO_BUFFER_SIZE, 0,
        state, read_packet, NULL, seek_payload);
    if (io == NULL) {
        av_free(io_buffer);
        return AVERROR(ENOMEM);
    }
    AVFormatContext *format = avformat_alloc_context();
    if (format == NULL) {
        av_freep(&io->buffer);
        avio_context_free(&io);
        return AVERROR(ENOMEM);
    }
    format->pb = io;
    format->flags |= AVFMT_FLAG_CUSTOM_IO;
    int result = avformat_open_input(&format, NULL, NULL, NULL);
    if (result >= 0) result = avformat_find_stream_info(format, NULL);
    if (result < 0) {
        avformat_close_input(&format);
        av_freep(&io->buffer);
        avio_context_free(&io);
        return result;
    }
    *out_io = io;
    *out_format = format;
    return 0;
}

static int64_t determine_timeline_origin_ms(const AVFormatContext *format) {
    int64_t origin_ms = INT64_MAX;
    for (unsigned int index = 0; index < format->nb_streams; index++) {
        const AVStream *stream = format->streams[index];
        enum AVMediaType type = stream->codecpar->codec_type;
        if (type != AVMEDIA_TYPE_VIDEO && type != AVMEDIA_TYPE_AUDIO) continue;
        if (stream->start_time == AV_NOPTS_VALUE) continue;
        int64_t candidate = rescale_ms_down(stream->start_time, stream->time_base);
        if (candidate < origin_ms) origin_ms = candidate;
    }
    if (origin_ms == INT64_MAX && format->start_time != AV_NOPTS_VALUE)
        origin_ms = av_rescale_q_rnd(format->start_time,
            (AVRational){1, AV_TIME_BASE}, (AVRational){1, 1000},
            AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
    return origin_ms == INT64_MAX ? 0 : origin_ms;
}

static int determine_duration(age_movie *movie, AVStream *stream, int64_t *out_duration_ms) {
    int64_t best_duration_ms = 0;
    if (stream->duration != AV_NOPTS_VALUE && stream->duration > 0) {
        int64_t candidate = rescale_ms_down(stream->duration, stream->time_base);
        if (candidate > best_duration_ms) best_duration_ms = candidate;
    }
    if (movie->video_format->duration != AV_NOPTS_VALUE && movie->video_format->duration > 0) {
        int64_t candidate = av_rescale_q_rnd(movie->video_format->duration,
            (AVRational){1, AV_TIME_BASE}, (AVRational){1, 1000},
            AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
        if (candidate > best_duration_ms) best_duration_ms = candidate;
    }

    int64_t first = AV_NOPTS_VALUE;
    int64_t end = AV_NOPTS_VALUE;
    int64_t video_packet_count = 0;
    AVPacket *probe = av_packet_alloc();
    if (probe == NULL) return AVERROR(ENOMEM);
    int result;
    int64_t media_end_ms = 0;
    while ((result = av_read_frame(movie->video_format, probe)) >= 0) {
        AVStream *packet_stream = movie->video_format->streams[probe->stream_index];
        enum AVMediaType packet_type = packet_stream->codecpar->codec_type;
        if ((packet_type == AVMEDIA_TYPE_VIDEO || packet_type == AVMEDIA_TYPE_AUDIO)
            && probe->pts != AV_NOPTS_VALUE) {
            int64_t packet_end = probe->pts;
            if (probe->duration > 0 && packet_end <= INT64_MAX - probe->duration)
                packet_end += probe->duration;
            int64_t candidate = rescale_ms_down(packet_end, packet_stream->time_base)
                                - movie->timeline_origin_ms;
            if (candidate > media_end_ms) media_end_ms = candidate;
        }
        if (probe->stream_index == movie->video_stream_index) {
            video_packet_count++;
            int64_t timestamp = probe->pts != AV_NOPTS_VALUE ? probe->pts : probe->dts;
            if (timestamp != AV_NOPTS_VALUE) {
                if (first == AV_NOPTS_VALUE || timestamp < first) first = timestamp;
                int64_t packet_end = timestamp;
                if (probe->duration > 0 && timestamp <= INT64_MAX - probe->duration) {
                    packet_end += probe->duration;
                }
                if (end == AV_NOPTS_VALUE || packet_end > end) end = packet_end;
            }
        }
        av_packet_unref(probe);
    }
    av_packet_free(&probe);
    if (result != AVERROR_EOF) return result;
    if (media_end_ms > best_duration_ms) best_duration_ms = media_end_ms;
    if (first != AV_NOPTS_VALUE && end != AV_NOPTS_VALUE && end > first) {
        int64_t candidate = rescale_ms_down(end - first, stream->time_base);
        if (candidate > best_duration_ms) best_duration_ms = candidate;
    }
    AVRational average_rate = stream->avg_frame_rate;
    if (video_packet_count > 0 && average_rate.num > 0 && average_rate.den > 0) {
        int64_t candidate = av_rescale_q_rnd(video_packet_count, av_inv_q(average_rate),
            (AVRational){1, 1000}, AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
        if (candidate > best_duration_ms) best_duration_ms = candidate;
    }
    *out_duration_ms = best_duration_ms;
    return best_duration_ms > 0 ? 0 : AVERROR_INVALIDDATA;
}

static int rewind_for_decode(age_movie *movie, AVStream *stream) {
    /* MPEG video can contain decodable preroll before AVStream.start_time. DirectShow
       presents those opening frames from graph time zero; seeking to the video pin's
       later declared start skips them and advances picture relative to audio. */
    int64_t target = av_rescale_q_rnd(movie->timeline_origin_ms,
        (AVRational){1, 1000}, stream->time_base,
        AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
    int result = av_seek_frame(movie->video_format, movie->video_stream_index, target, AVSEEK_FLAG_BACKWARD);
    if (result < 0) {
        movie->video_io_state.position = 0;
        if (movie->video_io != NULL) {
            avio_flush(movie->video_io);
            movie->video_io->eof_reached = 0;
            movie->video_io->error = 0;
        }
        result = avformat_seek_file(movie->video_format, movie->video_stream_index,
            INT64_MIN, target, INT64_MAX, 0);
    }
    if (result >= 0) avcodec_flush_buffers(movie->video_codec);
    return result;
}

static int rewind_audio_for_decode(age_movie *movie, AVStream *stream) {
    int64_t target = stream->start_time != AV_NOPTS_VALUE ? stream->start_time : 0;
    int result = av_seek_frame(movie->audio_format, movie->audio_stream_index, target, AVSEEK_FLAG_BACKWARD);
    if (result < 0) {
        movie->audio_io_state.position = 0;
        if (movie->audio_io != NULL) {
            avio_flush(movie->audio_io);
            movie->audio_io->eof_reached = 0;
            movie->audio_io->error = 0;
        }
        result = avformat_seek_file(movie->audio_format, movie->audio_stream_index,
            INT64_MIN, target, INT64_MAX, 0);
    }
    if (result >= 0) avcodec_flush_buffers(movie->audio_codec);
    return result;
}

static int64_t stream_timestamp_for_position(
    int64_t timeline_origin_ms, int64_t position_ms, AVRational time_base) {
    int64_t absolute_ms = timeline_origin_ms;
    if (position_ms > 0 && absolute_ms <= INT64_MAX - position_ms)
        absolute_ms += position_ms;
    return av_rescale_q_rnd(absolute_ms, (AVRational){1, 1000}, time_base,
        AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
}

static int seek_video_for_decode(age_movie *movie, int64_t position_ms) {
    int64_t target = stream_timestamp_for_position(
        movie->timeline_origin_ms, position_ms, movie->video_time_base);
    int result = av_seek_frame(
        movie->video_format, movie->video_stream_index, target, AVSEEK_FLAG_BACKWARD);
    if (result < 0)
        result = avformat_seek_file(movie->video_format, movie->video_stream_index,
            INT64_MIN, target, INT64_MAX, AVSEEK_FLAG_BACKWARD);
    if (result < 0) return result;

    avcodec_flush_buffers(movie->video_codec);
    if (movie->video_packet != NULL) av_packet_unref(movie->video_packet);
    if (movie->video_frame != NULL) av_frame_unref(movie->video_frame);
    movie->video_input_eof = 0;
    movie->video_decoder_draining = 0;
    movie->video_decoder_eof = 0;
    movie->video_seek_preroll = position_ms > 0;
    movie->decoded_video_frame_count = 0;
    movie->video_fallback_origin_ms = position_ms;
    return 0;
}

static int seek_audio_for_decode(age_movie *movie, int64_t position_ms) {
    if (movie->audio_stream_index < 0) return 0;
    int64_t target = stream_timestamp_for_position(
        movie->timeline_origin_ms, position_ms, movie->audio_time_base);
    int result = av_seek_frame(
        movie->audio_format, movie->audio_stream_index, target, AVSEEK_FLAG_BACKWARD);
    if (result < 0)
        result = avformat_seek_file(movie->audio_format, movie->audio_stream_index,
            INT64_MIN, target, INT64_MAX, AVSEEK_FLAG_BACKWARD);
    if (result < 0) return result;

    avcodec_flush_buffers(movie->audio_codec);
    if (movie->audio_packet != NULL) av_packet_unref(movie->audio_packet);
    if (movie->audio_frame != NULL) av_frame_unref(movie->audio_frame);
    movie->audio_input_eof = 0;
    movie->audio_decoder_draining = 0;
    movie->audio_decoder_eof = 0;
    movie->audio_frame_pending = 0;
    movie->audio_seek_preroll = position_ms > 0;
    movie->decoded_audio_sample_count = 0;
    movie->audio_fallback_origin_ms = position_ms;
    swr_close(movie->swr);
    return swr_init(movie->swr);
}

static void destroy_movie(age_movie *movie) {
    if (movie == NULL) return;
    swr_free(&movie->swr);
    sws_freeContext(movie->sws);
    av_frame_free(&movie->audio_frame);
    av_frame_free(&movie->rgba_frame);
    av_frame_free(&movie->video_frame);
    av_packet_free(&movie->audio_packet);
    av_packet_free(&movie->video_packet);
    avcodec_free_context(&movie->audio_codec);
    avcodec_free_context(&movie->video_codec);
    avformat_close_input(&movie->audio_format);
    avformat_close_input(&movie->video_format);
    if (movie->audio_io != NULL) {
        av_freep(&movie->audio_io->buffer);
        avio_context_free(&movie->audio_io);
    }
    if (movie->video_io != NULL) {
        av_freep(&movie->video_io->buffer);
        avio_context_free(&movie->video_io);
    }
    av_free(movie->payload);
    free(movie);
}

uint32_t AGE_MOVIE_CALL age_movie_abi_version(void) {
    return AGE_MOVIE_ABI_VERSION;
}

int32_t AGE_MOVIE_CALL age_movie_open(const uint8_t *bytes, size_t length,
    age_movie **out_movie, age_movie_info *out_info,
    char *error_buffer, size_t error_buffer_size) {
    if (out_movie != NULL) *out_movie = NULL;
    if (out_info != NULL) memset(out_info, 0, sizeof(*out_info));
    if (bytes == NULL || length == 0 || out_movie == NULL || out_info == NULL) {
        copy_error(error_buffer, error_buffer_size, "invalid movie open arguments");
        return AGE_MOVIE_INVALID_ARGUMENT;
    }

    age_movie *movie = (age_movie *)calloc(1, sizeof(*movie));
    if (movie == NULL) {
        copy_error(error_buffer, error_buffer_size, "movie allocation failed");
        return AGE_MOVIE_ERROR;
    }
    movie->video_stream_index = -1;
    movie->audio_stream_index = -1;

    if (length > SIZE_MAX - AV_INPUT_BUFFER_PADDING_SIZE) {
        snprintf(movie->error, sizeof(movie->error), "movie payload is too large");
        goto failure;
    }
    movie->payload = (uint8_t *)av_mallocz(length + AV_INPUT_BUFFER_PADDING_SIZE);
    if (movie->payload == NULL) {
        snprintf(movie->error, sizeof(movie->error), "movie payload allocation failed");
        goto failure;
    }
    memcpy(movie->payload, bytes, length);
    movie->payload_size = length;

    int result = open_format_context(movie, &movie->video_io_state,
        &movie->video_io, &movie->video_format);
    if (result < 0) {
        set_error(movie, "open MPEG program stream", result);
        goto failure;
    }
    movie->timeline_origin_ms = determine_timeline_origin_ms(movie->video_format);

    const AVCodec *video_decoder = NULL;
    result = av_find_best_stream(movie->video_format, AVMEDIA_TYPE_VIDEO, -1, -1, &video_decoder, 0);
    if (result < 0 || video_decoder == NULL) {
        set_error(movie, "find MPEG video stream", result < 0 ? result : AVERROR_DECODER_NOT_FOUND);
        goto failure;
    }
    movie->video_stream_index = result;
    AVStream *stream = movie->video_format->streams[movie->video_stream_index];
    movie->video_time_base = stream->time_base;
    movie->video_fallback_origin_ms = 0;

    movie->video_codec = avcodec_alloc_context3(video_decoder);
    if (movie->video_codec == NULL) {
        snprintf(movie->error, sizeof(movie->error), "video decoder allocation failed");
        goto failure;
    }
    result = avcodec_parameters_to_context(movie->video_codec, stream->codecpar);
    if (result < 0) {
        set_error(movie, "copy video parameters", result);
        goto failure;
    }
    result = avcodec_open2(movie->video_codec, video_decoder, NULL);
    if (result < 0) {
        set_error(movie, "open MPEG-1 video decoder", result);
        goto failure;
    }
    movie->width = movie->video_codec->width;
    movie->height = movie->video_codec->height;
    if (movie->width <= 0 || movie->height <= 0) {
        snprintf(movie->error, sizeof(movie->error), "invalid video dimensions %dx%d", movie->width, movie->height);
        goto failure;
    }
    if ((size_t)movie->width > SIZE_MAX / 4u / (size_t)movie->height) {
        snprintf(movie->error, sizeof(movie->error), "video dimensions overflow RGBA storage");
        goto failure;
    }

    int64_t duration_ms = 0;
    result = rewind_for_decode(movie, stream);
    if (result < 0) {
        set_error(movie, "rewind MPEG stream for duration scan", result);
        goto failure;
    }
    result = determine_duration(movie, stream, &duration_ms);
    if (result < 0) {
        set_error(movie, "determine video duration", result);
        goto failure;
    }
    movie->stop_time_ms = duration_ms;
    result = rewind_for_decode(movie, stream);
    if (result < 0) {
        set_error(movie, "rewind MPEG stream", result);
        goto failure;
    }

    movie->video_packet = av_packet_alloc();
    movie->video_frame = av_frame_alloc();
    movie->rgba_frame = av_frame_alloc();
    if (movie->video_packet == NULL || movie->video_frame == NULL || movie->rgba_frame == NULL) {
        snprintf(movie->error, sizeof(movie->error), "video packet/frame allocation failed");
        goto failure;
    }
    movie->rgba_frame->format = AV_PIX_FMT_RGBA;
    movie->rgba_frame->width = movie->width;
    movie->rgba_frame->height = movie->height;
    result = av_frame_get_buffer(movie->rgba_frame, 32);
    if (result < 0) {
        set_error(movie, "allocate aligned RGBA frame", result);
        goto failure;
    }

    const AVCodec *audio_decoder = NULL;
    result = av_find_best_stream(movie->video_format, AVMEDIA_TYPE_AUDIO, -1, -1, &audio_decoder, 0);
    if (result >= 0 && audio_decoder != NULL) {
        result = open_format_context(movie, &movie->audio_io_state,
            &movie->audio_io, &movie->audio_format);
        if (result < 0) {
            set_error(movie, "open MPEG audio program stream", result);
            goto failure;
        }
        result = av_find_best_stream(movie->audio_format, AVMEDIA_TYPE_AUDIO,
            -1, -1, &audio_decoder, 0);
        if (result < 0 || audio_decoder == NULL) {
            set_error(movie, "find MPEG audio stream",
                result < 0 ? result : AVERROR_DECODER_NOT_FOUND);
            goto failure;
        }
        movie->audio_stream_index = result;
        AVStream *audio_stream = movie->audio_format->streams[movie->audio_stream_index];
        movie->audio_time_base = audio_stream->time_base;
        movie->audio_fallback_origin_ms =
            audio_stream->start_time == AV_NOPTS_VALUE ? 0
            : rescale_ms_down(audio_stream->start_time, movie->audio_time_base)
              - movie->timeline_origin_ms;
        movie->audio_codec = avcodec_alloc_context3(audio_decoder);
        if (movie->audio_codec == NULL) {
            snprintf(movie->error, sizeof(movie->error), "audio decoder allocation failed");
            goto failure;
        }
        result = avcodec_parameters_to_context(movie->audio_codec, audio_stream->codecpar);
        if (result < 0) {
            set_error(movie, "copy audio parameters", result);
            goto failure;
        }
        result = avcodec_open2(movie->audio_codec, audio_decoder, NULL);
        if (result < 0) {
            set_error(movie, "open MPEG audio decoder", result);
            goto failure;
        }
        movie->audio_sample_rate = movie->audio_codec->sample_rate;
        if (movie->audio_sample_rate <= 0) {
            snprintf(movie->error, sizeof(movie->error), "invalid audio sample rate %d",
                movie->audio_sample_rate);
            goto failure;
        }
        AVChannelLayout stereo_layout = AV_CHANNEL_LAYOUT_STEREO;
        result = swr_alloc_set_opts2(&movie->swr,
            &stereo_layout, AV_SAMPLE_FMT_FLT, movie->audio_sample_rate,
            &movie->audio_codec->ch_layout, movie->audio_codec->sample_fmt,
            movie->audio_sample_rate, 0, NULL);
        if (result < 0 || movie->swr == NULL) {
            set_error(movie, "configure stereo PCM conversion",
                result < 0 ? result : AVERROR(ENOMEM));
            goto failure;
        }
        result = swr_init(movie->swr);
        if (result < 0) {
            set_error(movie, "initialize stereo PCM conversion", result);
            goto failure;
        }
        result = rewind_audio_for_decode(movie, audio_stream);
        if (result < 0) {
            set_error(movie, "rewind MPEG audio stream", result);
            goto failure;
        }
        movie->audio_packet = av_packet_alloc();
        movie->audio_frame = av_frame_alloc();
        if (movie->audio_packet == NULL || movie->audio_frame == NULL) {
            snprintf(movie->error, sizeof(movie->error), "audio packet/frame allocation failed");
            goto failure;
        }
    }

    AVRational frame_rate = av_guess_frame_rate(movie->video_format, stream, NULL);
    out_info->width = movie->width;
    out_info->height = movie->height;
    out_info->stop_time_ms = duration_ms;
    out_info->frame_rate_num = frame_rate.num;
    out_info->frame_rate_den = frame_rate.den;
    if (movie->audio_stream_index >= 0) {
        out_info->has_audio = 1;
        out_info->audio_sample_rate = movie->audio_sample_rate;
        out_info->audio_channels = 2;
        out_info->audio_frame_samples = movie->audio_codec->frame_size > 0
            ? movie->audio_codec->frame_size : 4096;
    }
    *out_movie = movie;
    copy_error(error_buffer, error_buffer_size, "");
    return 0;

failure:
    copy_error(error_buffer, error_buffer_size, movie->error);
    destroy_movie(movie);
    return AGE_MOVIE_ERROR;
}

int32_t AGE_MOVIE_CALL age_movie_seek(age_movie *movie, int64_t position_ms) {
    if (movie == NULL || position_ms < 0) return AGE_MOVIE_INVALID_ARGUMENT;
    if (movie->stop_time_ms > 0 && position_ms >= movie->stop_time_ms)
        position_ms = movie->stop_time_ms - 1;

    int result = seek_video_for_decode(movie, position_ms);
    if (result < 0) {
        set_error(movie, "seek MPEG video stream", result);
        return AGE_MOVIE_ERROR;
    }
    result = seek_audio_for_decode(movie, position_ms);
    if (result < 0) {
        set_error(movie, "seek MPEG audio stream", result);
        return AGE_MOVIE_ERROR;
    }
    return 0;
}

int32_t AGE_MOVIE_CALL age_movie_decode_video(age_movie *movie,
    uint8_t *rgba, size_t rgba_size, int64_t *out_pts_ms) {
    if (movie == NULL || rgba == NULL || out_pts_ms == NULL) return AGE_MOVIE_INVALID_ARGUMENT;
    size_t required = (size_t)movie->width * (size_t)movie->height * 4u;
    if (rgba_size < required) return AGE_MOVIE_BUFFER_TOO_SMALL;
    if (movie->video_decoder_eof) return AGE_MOVIE_EOF;

    for (;;) {
        int result = avcodec_receive_frame(movie->video_codec, movie->video_frame);
        if (result == 0) {
            movie->video_seek_preroll = 0;
            movie->sws = sws_getCachedContext(movie->sws,
                movie->video_frame->width, movie->video_frame->height,
                (enum AVPixelFormat)movie->video_frame->format,
                movie->width, movie->height, AV_PIX_FMT_RGBA,
                SWS_BILINEAR, NULL, NULL, NULL);
            if (movie->sws == NULL) {
                snprintf(movie->error, sizeof(movie->error), "create RGBA conversion context failed");
                return AGE_MOVIE_ERROR;
            }
            result = av_frame_make_writable(movie->rgba_frame);
            if (result < 0) {
                set_error(movie, "make aligned RGBA frame writable", result);
                return AGE_MOVIE_ERROR;
            }
            result = sws_scale(movie->sws,
                (const uint8_t * const *)movie->video_frame->data, movie->video_frame->linesize,
                0, movie->video_frame->height, movie->rgba_frame->data, movie->rgba_frame->linesize);
            if (result != movie->height) {
                snprintf(movie->error, sizeof(movie->error), "RGBA conversion returned %d of %d rows", result, movie->height);
                return AGE_MOVIE_ERROR;
            }
            size_t row_size = (size_t)movie->width * 4u;
            for (int row = 0; row < movie->height; row++) {
                memcpy(rgba + (size_t)row * row_size,
                    movie->rgba_frame->data[0] + (ptrdiff_t)row * movie->rgba_frame->linesize[0],
                    row_size);
            }

            int64_t timestamp = movie->video_frame->best_effort_timestamp;
            if (timestamp == AV_NOPTS_VALUE) {
                timestamp = movie->video_frame->pts;
            }
            if (timestamp == AV_NOPTS_VALUE) {
                AVRational rate = av_guess_frame_rate(movie->video_format,
                    movie->video_format->streams[movie->video_stream_index], movie->video_frame);
                if (rate.num <= 0 || rate.den <= 0) {
                    snprintf(movie->error, sizeof(movie->error), "decoded frame has no timestamp or frame rate");
                    return AGE_MOVIE_ERROR;
                }
                *out_pts_ms = movie->video_fallback_origin_ms
                    + av_rescale_q_rnd(movie->decoded_video_frame_count, av_inv_q(rate),
                    (AVRational){1, 1000}, AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
            } else {
                *out_pts_ms = rescale_ms_down(timestamp, movie->video_time_base)
                              - movie->timeline_origin_ms;
            }
            if (*out_pts_ms < 0) *out_pts_ms = 0;
            movie->decoded_video_frame_count++;
            av_frame_unref(movie->video_frame);
            return AGE_MOVIE_FRAME;
        }
        if (result == AVERROR_EOF) {
            movie->video_decoder_eof = 1;
            return AGE_MOVIE_EOF;
        }
        if (result != AVERROR(EAGAIN)) {
            set_error(movie, "receive decoded video frame", result);
            return AGE_MOVIE_ERROR;
        }

        if (movie->video_input_eof) {
            if (!movie->video_decoder_draining) {
                result = avcodec_send_packet(movie->video_codec, NULL);
                if (result < 0 && result != AVERROR_EOF) {
                    set_error(movie, "flush MPEG-1 video decoder", result);
                    return AGE_MOVIE_ERROR;
                }
                movie->video_decoder_draining = 1;
                continue;
            }
            snprintf(movie->error, sizeof(movie->error), "video decoder requested input after drain");
            return AGE_MOVIE_ERROR;
        }

        for (;;) {
            result = av_read_frame(movie->video_format, movie->video_packet);
            if (result == AVERROR_EOF) {
                movie->video_input_eof = 1;
                break;
            }
            if (result < 0) {
                set_error(movie, "read MPEG video packet", result);
                return AGE_MOVIE_ERROR;
            }
            if (movie->video_packet->stream_index != movie->video_stream_index) {
                av_packet_unref(movie->video_packet);
                continue;
            }
            break;
        }

        if (movie->video_input_eof) continue;
        result = avcodec_send_packet(movie->video_codec, movie->video_packet);
        av_packet_unref(movie->video_packet);
        if (result == AVERROR_INVALIDDATA && movie->video_seek_preroll)
            continue;
        if (result < 0) {
            set_error(movie, "send MPEG video packet", result);
            return AGE_MOVIE_ERROR;
        }
    }
}

int32_t AGE_MOVIE_CALL age_movie_decode_audio(age_movie *movie,
    float *stereo, size_t frame_capacity, int32_t *out_frame_count, int64_t *out_pts_ms) {
    if (out_frame_count != NULL) *out_frame_count = 0;
    if (movie == NULL || out_frame_count == NULL || out_pts_ms == NULL)
        return AGE_MOVIE_INVALID_ARGUMENT;
    if (movie->audio_stream_index < 0) return AGE_MOVIE_EOF;
    if (movie->audio_decoder_eof) return AGE_MOVIE_EOF;

    for (;;) {
        if (movie->audio_frame_pending) {
            int required = swr_get_out_samples(movie->swr, movie->audio_frame->nb_samples);
            if (required < 0) {
                set_error(movie, "size stereo PCM conversion", required);
                return AGE_MOVIE_ERROR;
            }
            *out_frame_count = required;
            if (stereo == NULL || frame_capacity < (size_t)required)
                return AGE_MOVIE_BUFFER_TOO_SMALL;

            int64_t timestamp = movie->audio_frame->best_effort_timestamp;
            if (timestamp == AV_NOPTS_VALUE) timestamp = movie->audio_frame->pts;
            if (timestamp == AV_NOPTS_VALUE) {
                *out_pts_ms = movie->audio_fallback_origin_ms
                    + av_rescale_q_rnd(movie->decoded_audio_sample_count,
                    (AVRational){1, movie->audio_sample_rate}, (AVRational){1, 1000},
                    AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
            } else {
                *out_pts_ms = rescale_ms_down(timestamp, movie->audio_time_base)
                              - movie->timeline_origin_ms;
            }
            if (*out_pts_ms < 0) *out_pts_ms = 0;

            uint8_t *output[1] = {(uint8_t *)stereo};
            int converted = swr_convert(movie->swr, output, (int)frame_capacity,
                (const uint8_t **)movie->audio_frame->extended_data,
                movie->audio_frame->nb_samples);
            if (converted < 0) {
                set_error(movie, "convert MPEG audio to stereo PCM", converted);
                return AGE_MOVIE_ERROR;
            }
            *out_frame_count = converted;
            movie->decoded_audio_sample_count += converted;
            movie->audio_frame_pending = 0;
            av_frame_unref(movie->audio_frame);
            return AGE_MOVIE_FRAME;
        }

        int result = avcodec_receive_frame(movie->audio_codec, movie->audio_frame);
        if (result == 0) {
            movie->audio_seek_preroll = 0;
            movie->audio_frame_pending = 1;
            continue;
        }
        if (result == AVERROR_EOF) {
            movie->audio_decoder_eof = 1;
            return AGE_MOVIE_EOF;
        }
        if (result != AVERROR(EAGAIN)) {
            set_error(movie, "receive decoded audio frame", result);
            return AGE_MOVIE_ERROR;
        }

        if (movie->audio_input_eof) {
            if (!movie->audio_decoder_draining) {
                result = avcodec_send_packet(movie->audio_codec, NULL);
                if (result < 0 && result != AVERROR_EOF) {
                    set_error(movie, "flush MPEG audio decoder", result);
                    return AGE_MOVIE_ERROR;
                }
                movie->audio_decoder_draining = 1;
                continue;
            }
            snprintf(movie->error, sizeof(movie->error), "audio decoder requested input after drain");
            return AGE_MOVIE_ERROR;
        }

        for (;;) {
            result = av_read_frame(movie->audio_format, movie->audio_packet);
            if (result == AVERROR_EOF) {
                movie->audio_input_eof = 1;
                break;
            }
            if (result < 0) {
                set_error(movie, "read MPEG audio packet", result);
                return AGE_MOVIE_ERROR;
            }
            if (movie->audio_packet->stream_index != movie->audio_stream_index) {
                av_packet_unref(movie->audio_packet);
                continue;
            }
            break;
        }

        if (movie->audio_input_eof) continue;
        result = avcodec_send_packet(movie->audio_codec, movie->audio_packet);
        av_packet_unref(movie->audio_packet);
        if (result == AVERROR_INVALIDDATA && movie->audio_seek_preroll)
            continue;
        if (result < 0) {
            set_error(movie, "send MPEG audio packet", result);
            return AGE_MOVIE_ERROR;
        }
    }
}

const char *AGE_MOVIE_CALL age_movie_last_error(const age_movie *movie) {
    return movie != NULL ? movie->error : "movie handle is null";
}

void AGE_MOVIE_CALL age_movie_close(age_movie *movie) {
    destroy_movie(movie);
}
