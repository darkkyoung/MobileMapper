#pragma once
#include <stdint.h>
#ifdef MM_EXPORTS
#define MM_API extern "C" __declspec(dllexport)
#else
#define MM_API extern "C" __declspec(dllimport)
#endif
struct mm_stats { uint64_t decoded, presented, replaced, generation; int32_t width, height, error; };
MM_API unsigned mm_abi_version() noexcept;
MM_API const char* mm_codec_license() noexcept;
MM_API const char* mm_codec_configuration() noexcept;
MM_API int mm_create(void** handle, int force_warp) noexcept;
MM_API int mm_get_swapchain(void* handle, void** chain) noexcept;
MM_API int mm_resize(void* handle, int width, int height, float scale_x, float scale_y) noexcept;
MM_API int mm_reset(void* handle, uint64_t generation) noexcept;
MM_API int mm_decode(void* handle, const uint8_t* data, int length, int64_t pts, int config) noexcept;
MM_API int mm_get_stats(void* handle, mm_stats* stats) noexcept;
MM_API void mm_destroy(void* handle) noexcept;
