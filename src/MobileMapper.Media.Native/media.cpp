extern "C" {
#include <libavcodec/avcodec.h>
__declspec(dllexport) unsigned mm_abi_version() noexcept { return 1; }
__declspec(dllexport) const char* mm_codec_license() noexcept { return avcodec_license(); }
}
