#ifndef OME_MOVIE_H
#define OME_MOVIE_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define OME_MOVIE_API __declspec(dllexport)
#define OME_MOVIE_CALL __cdecl
#else
#define OME_MOVIE_API __attribute__((visibility("default")))
#define OME_MOVIE_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct ome_movie ome_movie;

typedef struct ome_movie_info {
    int32_t width;
    int32_t height;
    int64_t stop_time_ms;
    int32_t frame_rate_num;
    int32_t frame_rate_den;
    int32_t has_audio;
    int32_t audio_sample_rate;
    int32_t audio_channels;
    int32_t audio_frame_samples;
} ome_movie_info;

enum ome_movie_result {
    OME_MOVIE_EOF = 0,
    OME_MOVIE_FRAME = 1,
    OME_MOVIE_ERROR = -1,
    OME_MOVIE_INVALID_ARGUMENT = -2,
    OME_MOVIE_BUFFER_TOO_SMALL = -3
};

OME_MOVIE_API uint32_t OME_MOVIE_CALL ome_movie_abi_version(void);

/* Copies bytes before returning; the caller's buffer is borrowed only during this call. */
OME_MOVIE_API int32_t OME_MOVIE_CALL ome_movie_open(
    const uint8_t *bytes,
    size_t length,
    ome_movie **out_movie,
    ome_movie_info *out_info,
    char *error_buffer,
    size_t error_buffer_size);

/*
 * Seeks both the video and audio demux/decoder pipelines to the keyframe at or before
 * position_ms. The caller performs decoded-sample preroll to the exact requested position.
 */
OME_MOVIE_API int32_t OME_MOVIE_CALL ome_movie_seek(
    ome_movie *movie,
    int64_t position_ms);

/* Writes one tightly packed top-down RGBA8 frame and its zero-based presentation timestamp. */
OME_MOVIE_API int32_t OME_MOVIE_CALL ome_movie_decode_video(
    ome_movie *movie,
    uint8_t *rgba,
    size_t rgba_size,
    int64_t *out_pts_ms);

/*
 * Writes one interleaved stereo float32 PCM block and its presentation timestamp.
 * out_frame_count is also populated with the required frame count when the buffer is too small.
 */
OME_MOVIE_API int32_t OME_MOVIE_CALL ome_movie_decode_audio(
    ome_movie *movie,
    float *stereo,
    size_t frame_capacity,
    int32_t *out_frame_count,
    int64_t *out_pts_ms);

OME_MOVIE_API const char *OME_MOVIE_CALL ome_movie_last_error(const ome_movie *movie);
OME_MOVIE_API void OME_MOVIE_CALL ome_movie_close(ome_movie *movie);

#ifdef __cplusplus
}
#endif

#endif
