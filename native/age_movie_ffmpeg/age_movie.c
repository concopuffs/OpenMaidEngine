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
#include <libswscale/swscale.h>

#define AGE_MOVIE_ABI_VERSION 1u
#define AGE_MOVIE_IO_BUFFER_SIZE 32768
#define AGE_MOVIE_ERROR_SIZE 512

struct age_movie {
    uint8_t *payload;
    size_t payload_size;
    size_t payload_position;
    AVIOContext *io;
    AVFormatContext *format;
    AVCodecContext *video_codec;
    AVPacket *packet;
    AVFrame *frame;
    AVFrame *rgba_frame;
    struct SwsContext *sws;
    int video_stream_index;
    AVRational video_time_base;
    int64_t timestamp_origin;
    int64_t first_frame_timestamp;
    int input_eof;
    int decoder_draining;
    int decoder_eof;
    int64_t decoded_frame_count;
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
    age_movie *movie = (age_movie *)opaque;
    if (movie == NULL || buffer == NULL || buffer_size <= 0) {
        return AVERROR(EINVAL);
    }
    if (movie->payload_position >= movie->payload_size) {
        return AVERROR_EOF;
    }
    size_t remaining = movie->payload_size - movie->payload_position;
    size_t requested = (size_t)buffer_size;
    size_t count = remaining < requested ? remaining : requested;
    memcpy(buffer, movie->payload + movie->payload_position, count);
    movie->payload_position += count;
    return (int)count;
}

static int64_t seek_payload(void *opaque, int64_t offset, int whence) {
    age_movie *movie = (age_movie *)opaque;
    if (movie == NULL) {
        return AVERROR(EINVAL);
    }
    if ((whence & AVSEEK_SIZE) != 0) {
        return movie->payload_size <= INT64_MAX ? (int64_t)movie->payload_size : AVERROR(EOVERFLOW);
    }

    int base_whence = whence & ~AVSEEK_FORCE;
    int64_t base;
    switch (base_whence) {
        case SEEK_SET: base = 0; break;
        case SEEK_CUR:
            if (movie->payload_position > INT64_MAX) return AVERROR(EOVERFLOW);
            base = (int64_t)movie->payload_position;
            break;
        case SEEK_END:
            if (movie->payload_size > INT64_MAX) return AVERROR(EOVERFLOW);
            base = (int64_t)movie->payload_size;
            break;
        default: return AVERROR(EINVAL);
    }
    if (offset == INT64_MIN || (offset > 0 && base > INT64_MAX - offset) ||
        (offset < 0 && base < -offset)) {
        return AVERROR(EOVERFLOW);
    }
    int64_t target = base + offset;
    if (target < 0 || (uint64_t)target > movie->payload_size) {
        return AVERROR(EINVAL);
    }
    movie->payload_position = (size_t)target;
    return target;
}

static int64_t rescale_ms_down(int64_t value, AVRational time_base) {
    return av_rescale_q_rnd(value, time_base, (AVRational){1, 1000},
        AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
}

static int determine_duration(age_movie *movie, AVStream *stream, int64_t *out_duration_ms) {
    int64_t best_duration_ms = 0;
    if (stream->duration != AV_NOPTS_VALUE && stream->duration > 0) {
        int64_t candidate = rescale_ms_down(stream->duration, stream->time_base);
        if (candidate > best_duration_ms) best_duration_ms = candidate;
    }
    if (movie->format->duration != AV_NOPTS_VALUE && movie->format->duration > 0) {
        int64_t candidate = av_rescale_q_rnd(movie->format->duration,
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
    while ((result = av_read_frame(movie->format, probe)) >= 0) {
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
    int64_t target = stream->start_time != AV_NOPTS_VALUE ? stream->start_time : 0;
    int result = av_seek_frame(movie->format, movie->video_stream_index, target, AVSEEK_FLAG_BACKWARD);
    if (result < 0) {
        movie->payload_position = 0;
        if (movie->io != NULL) {
            avio_flush(movie->io);
            movie->io->eof_reached = 0;
            movie->io->error = 0;
        }
        result = avformat_seek_file(movie->format, movie->video_stream_index,
            INT64_MIN, target, INT64_MAX, 0);
    }
    if (result >= 0) avcodec_flush_buffers(movie->video_codec);
    return result;
}

static void destroy_movie(age_movie *movie) {
    if (movie == NULL) return;
    sws_freeContext(movie->sws);
    av_frame_free(&movie->rgba_frame);
    av_frame_free(&movie->frame);
    av_packet_free(&movie->packet);
    avcodec_free_context(&movie->video_codec);
    avformat_close_input(&movie->format);
    if (movie->io != NULL) {
        av_freep(&movie->io->buffer);
        avio_context_free(&movie->io);
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
    movie->timestamp_origin = AV_NOPTS_VALUE;
    movie->first_frame_timestamp = AV_NOPTS_VALUE;

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

    uint8_t *io_buffer = (uint8_t *)av_malloc(AGE_MOVIE_IO_BUFFER_SIZE);
    if (io_buffer == NULL) {
        snprintf(movie->error, sizeof(movie->error), "movie IO buffer allocation failed");
        goto failure;
    }
    movie->io = avio_alloc_context(io_buffer, AGE_MOVIE_IO_BUFFER_SIZE, 0,
        movie, read_packet, NULL, seek_payload);
    if (movie->io == NULL) {
        av_free(io_buffer);
        snprintf(movie->error, sizeof(movie->error), "movie AVIO context allocation failed");
        goto failure;
    }

    movie->format = avformat_alloc_context();
    if (movie->format == NULL) {
        snprintf(movie->error, sizeof(movie->error), "movie format context allocation failed");
        goto failure;
    }
    movie->format->pb = movie->io;
    movie->format->flags |= AVFMT_FLAG_CUSTOM_IO;
    int result = avformat_open_input(&movie->format, NULL, NULL, NULL);
    if (result < 0) {
        set_error(movie, "open MPEG program stream", result);
        goto failure;
    }
    result = avformat_find_stream_info(movie->format, NULL);
    if (result < 0) {
        set_error(movie, "probe MPEG streams", result);
        goto failure;
    }

    const AVCodec *video_decoder = NULL;
    result = av_find_best_stream(movie->format, AVMEDIA_TYPE_VIDEO, -1, -1, &video_decoder, 0);
    if (result < 0 || video_decoder == NULL) {
        set_error(movie, "find MPEG video stream", result < 0 ? result : AVERROR_DECODER_NOT_FOUND);
        goto failure;
    }
    movie->video_stream_index = result;
    AVStream *stream = movie->format->streams[movie->video_stream_index];
    movie->video_time_base = stream->time_base;
    movie->timestamp_origin = stream->start_time;

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
    result = rewind_for_decode(movie, stream);
    if (result < 0) {
        set_error(movie, "rewind MPEG stream", result);
        goto failure;
    }

    movie->packet = av_packet_alloc();
    movie->frame = av_frame_alloc();
    movie->rgba_frame = av_frame_alloc();
    if (movie->packet == NULL || movie->frame == NULL || movie->rgba_frame == NULL) {
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

    AVRational frame_rate = av_guess_frame_rate(movie->format, stream, NULL);
    out_info->width = movie->width;
    out_info->height = movie->height;
    out_info->stop_time_ms = duration_ms;
    out_info->frame_rate_num = frame_rate.num;
    out_info->frame_rate_den = frame_rate.den;
    for (unsigned int index = 0; index < movie->format->nb_streams; index++) {
        if (movie->format->streams[index]->codecpar->codec_type == AVMEDIA_TYPE_AUDIO) {
            out_info->has_audio = 1;
            break;
        }
    }
    *out_movie = movie;
    copy_error(error_buffer, error_buffer_size, "");
    return 0;

failure:
    copy_error(error_buffer, error_buffer_size, movie->error);
    destroy_movie(movie);
    return AGE_MOVIE_ERROR;
}

int32_t AGE_MOVIE_CALL age_movie_decode_video(age_movie *movie,
    uint8_t *rgba, size_t rgba_size, int64_t *out_pts_ms) {
    if (movie == NULL || rgba == NULL || out_pts_ms == NULL) return AGE_MOVIE_INVALID_ARGUMENT;
    size_t required = (size_t)movie->width * (size_t)movie->height * 4u;
    if (rgba_size < required) return AGE_MOVIE_BUFFER_TOO_SMALL;
    if (movie->decoder_eof) return AGE_MOVIE_EOF;

    for (;;) {
        int result = avcodec_receive_frame(movie->video_codec, movie->frame);
        if (result == 0) {
            movie->sws = sws_getCachedContext(movie->sws,
                movie->frame->width, movie->frame->height, (enum AVPixelFormat)movie->frame->format,
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
                (const uint8_t * const *)movie->frame->data, movie->frame->linesize,
                0, movie->frame->height, movie->rgba_frame->data, movie->rgba_frame->linesize);
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

            int64_t timestamp = movie->frame->best_effort_timestamp;
            if (timestamp == AV_NOPTS_VALUE) {
                timestamp = movie->frame->pts;
            }
            if (timestamp == AV_NOPTS_VALUE) {
                AVRational rate = av_guess_frame_rate(movie->format,
                    movie->format->streams[movie->video_stream_index], movie->frame);
                if (rate.num <= 0 || rate.den <= 0) {
                    snprintf(movie->error, sizeof(movie->error), "decoded frame has no timestamp or frame rate");
                    return AGE_MOVIE_ERROR;
                }
                *out_pts_ms = av_rescale_q_rnd(movie->decoded_frame_count, av_inv_q(rate),
                    (AVRational){1, 1000}, AV_ROUND_DOWN | AV_ROUND_PASS_MINMAX);
            } else {
                if (movie->timestamp_origin == AV_NOPTS_VALUE) {
                    if (movie->first_frame_timestamp == AV_NOPTS_VALUE) movie->first_frame_timestamp = timestamp;
                    movie->timestamp_origin = movie->first_frame_timestamp;
                }
                int64_t relative = timestamp >= movie->timestamp_origin ? timestamp - movie->timestamp_origin : 0;
                *out_pts_ms = rescale_ms_down(relative, movie->video_time_base);
            }
            movie->decoded_frame_count++;
            av_frame_unref(movie->frame);
            return AGE_MOVIE_FRAME;
        }
        if (result == AVERROR_EOF) {
            movie->decoder_eof = 1;
            return AGE_MOVIE_EOF;
        }
        if (result != AVERROR(EAGAIN)) {
            set_error(movie, "receive decoded video frame", result);
            return AGE_MOVIE_ERROR;
        }

        if (movie->input_eof) {
            if (!movie->decoder_draining) {
                result = avcodec_send_packet(movie->video_codec, NULL);
                if (result < 0 && result != AVERROR_EOF) {
                    set_error(movie, "flush MPEG-1 video decoder", result);
                    return AGE_MOVIE_ERROR;
                }
                movie->decoder_draining = 1;
                continue;
            }
            snprintf(movie->error, sizeof(movie->error), "video decoder requested input after drain");
            return AGE_MOVIE_ERROR;
        }

        for (;;) {
            result = av_read_frame(movie->format, movie->packet);
            if (result == AVERROR_EOF) {
                movie->input_eof = 1;
                break;
            }
            if (result < 0) {
                set_error(movie, "read MPEG video packet", result);
                return AGE_MOVIE_ERROR;
            }
            if (movie->packet->stream_index != movie->video_stream_index) {
                av_packet_unref(movie->packet);
                continue;
            }
            break;
        }

        if (movie->input_eof) continue;
        result = avcodec_send_packet(movie->video_codec, movie->packet);
        av_packet_unref(movie->packet);
        if (result < 0) {
            set_error(movie, "send MPEG video packet", result);
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
