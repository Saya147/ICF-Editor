#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <bcrypt.h>
#include <cstdint>
#include <cstring>
#include <vector>

__declspec(noinline) static void build_secret(uint8_t key[16], uint8_t iv[16]) {
    volatile uint8_t km[16] = {0xA3,0x61,0xF4,0x57,0x9A,0x63,0x00,0x45,0x92,0xAE,0x7A,0x0D,0x49,0x50,0xDB,0x8A};
    volatile uint8_t kx[16] = {0xAA,0xAB,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA,0xAA};
    volatile uint8_t vm[16] = {0x1B,0xFF,0x68,0x86,0x84,0xD5,0xAE,0x3B,0x50,0xD5,0xA5,0x76,0x8B,0xD0,0x55,0x3A};
    for (int i = 0; i < 16; ++i) { key[i] = static_cast<uint8_t>(km[i] ^ kx[i]); iv[i] = static_cast<uint8_t>(vm[i] ^ 0xAA); }
}

extern "C" __declspec(dllexport) uint32_t __cdecl icf_crc32(const uint8_t* data, int length) {
    if (!data || length < 0) return 0;
    uint32_t crc = 0xFFFFFFFFu;
    for (int i = 0; i < length; ++i) {
        crc ^= data[i];
        for (int bit = 0; bit < 8; ++bit) crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1u)));
    }
    return ~crc;
}

extern "C" __declspec(dllexport) int __cdecl icf_crypt(const uint8_t* input, int length, uint8_t* output, int encrypt) {
    if (!input || !output || length <= 0 || (length & 15) != 0) return -1;
    BCRYPT_ALG_HANDLE algorithm = nullptr;
    BCRYPT_KEY_HANDLE key_handle = nullptr;
    DWORD object_length = 0, result = 0, written = 0;
    std::vector<uint8_t> key_object;
    uint8_t key[16], iv[16]; build_secret(key, iv);
    NTSTATUS status = BCryptOpenAlgorithmProvider(&algorithm, BCRYPT_AES_ALGORITHM, nullptr, 0);
    if (status >= 0) status = BCryptSetProperty(algorithm, BCRYPT_CHAINING_MODE,
        reinterpret_cast<PUCHAR>(const_cast<wchar_t*>(BCRYPT_CHAIN_MODE_CBC)),
        sizeof(BCRYPT_CHAIN_MODE_CBC), 0);
    if (status >= 0) status = BCryptGetProperty(algorithm, BCRYPT_OBJECT_LENGTH,
        reinterpret_cast<PUCHAR>(&object_length), sizeof(object_length), &result, 0);
    if (status >= 0) {
        key_object.resize(object_length);
        status = BCryptGenerateSymmetricKey(algorithm, &key_handle, key_object.data(), object_length, key, sizeof(key), 0);
    }
    if (status >= 0) {
        status = encrypt
            ? BCryptEncrypt(key_handle, const_cast<PUCHAR>(input), length, nullptr, iv, sizeof(iv), output, length, &written, 0)
            : BCryptDecrypt(key_handle, const_cast<PUCHAR>(input), length, nullptr, iv, sizeof(iv), output, length, &written, 0);
    }
    SecureZeroMemory(key, sizeof(key)); SecureZeroMemory(iv, sizeof(iv));
    if (!key_object.empty()) SecureZeroMemory(key_object.data(), key_object.size());
    if (key_handle) BCryptDestroyKey(key_handle);
    if (algorithm) BCryptCloseAlgorithmProvider(algorithm, 0);
    return status >= 0 && written == static_cast<DWORD>(length) ? 0 : static_cast<int>(status < 0 ? status : -2);
}

BOOL WINAPI DllMain(HINSTANCE, DWORD, LPVOID) { return TRUE; }
