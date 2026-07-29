#ifndef AGE_MOVIE_H
#define AGE_MOVIE_H

#include <stddef.h>
#include <stdint.h>

#if defined(_WIN32)
#define AGE_MOVIE_API __declspec(dllexport)
#define AGE_MOVIE_CALL __cdecl
#else
#define AGE_MOVIE_API __attribute__((visibility("default")))
#define AGE_MOVIE_CALL
#endif

#ifdef __cplusplus
extern "C" {
#endif

typedef struct age_movie age_movie;

typedef struct age_movie_info {
    int32_t width;
    int32_t height;
    int64_t stop_time_ms;
    int32_t frame_rate_num;
    int32_t frame_rate_den;
    int32_t has_audio;
    int32_t audio_sample_rate;
    int32_t audio_channels;
    int32_t audio_frame_samples;
} age_movie_info;

enum age_movie_result {
    AGE_MOVIE_EOF = 0,
    AGE_MOVIE_FRAME = 1,
    AGE_MOVIE_ERROR = -1,
    AGE_MOVIE_INVALID_ARGUMENT = -2,
    AGE_MOVIE_BUFFER_TOO_SMALL = -3
};

AGE_MOVIE_API uint32_t AGE_MOVIE_CALL age_movie_abi_version(void);

/* Copies bytes before returning; the caller's buffer is borrowed only during this call. */
AGE_MOVIE_API int32_t AGE_MOVIE_CALL age_movie_open(
    const uint8_t *bytes,
    size_t length,
    age_movie **out_movie,
    age_movie_info *out_info,
    char *error_buffer,
    size_t error_buffer_size);

/*
 * Seeks both the video and audio demux/decoder pipelines to the keyframe at or before
 * position_ms. The caller performs decoded-sample preroll to the exact requested position.
 */
AGE_MOVIE_API int32_t AGE_MOVIE_CALL age_movie_seek(
    age_movie *movie,
    int64_t position_ms);

/* Writes one tightly packed top-down RGBA8 frame and its zero-based presentation timestamp. */
AGE_MOVIE_API int32_t AGE_MOVIE_CALL age_movie_decode_video(
    age_movie *movie,
    uint8_t *rgba,
    size_t rgba_size,
    int64_t *out_pts_ms);

/*
 * Writes one interleaved stereo float32 PCM block and its presentation timestamp.
 * out_frame_count is also populated with the required frame count when the buffer is too small.
 */
AGE_MOVIE_API int32_t AGE_MOVIE_CALL age_movie_decode_audio(
    age_movie *movie,
    float *stereo,
    size_t frame_capacity,
    int32_t *out_frame_count,
    int64_t *out_pts_ms);

AGE_MOVIE_API const char *AGE_MOVIE_CALL age_movie_last_error(const age_movie *movie);
AGE_MOVIE_API void AGE_MOVIE_CALL age_movie_close(age_movie *movie);

#ifdef __cplusplus
}
#endif

#endif
