cmake_minimum_required(VERSION 3.16)
project(oscarwatch_ft8 C)

set(CMAKE_C_STANDARD 99)
set(CMAKE_C_STANDARD_REQUIRED ON)
set(CMAKE_POSITION_INDEPENDENT_CODE ON)

set(FT8_ROOT "${CMAKE_CURRENT_SOURCE_DIR}/../ft8_lib")

# Float dB waterfall instead of 0.5 dB steps in a byte. Every source that
# includes decode.h must agree, so it is set for the whole project.
option(OW_FT8_FLOAT_WATERFALL "Store the decode waterfall as float dB" OFF)
if(OW_FT8_FLOAT_WATERFALL)
  add_compile_definitions(WATERFALL_USE_FLOAT)
endif()

set(FT8_SOURCES
  ${FT8_ROOT}/ft8/constants.c
  ${FT8_ROOT}/ft8/crc.c
  ${FT8_ROOT}/ft8/decode.c
  ${FT8_ROOT}/ft8/encode.c
  ${FT8_ROOT}/ft8/ldpc.c
  ${FT8_ROOT}/ft8/message.c
  ${FT8_ROOT}/ft8/text.c
  ${FT8_ROOT}/common/monitor.c
  ${FT8_ROOT}/fft/kiss_fft.c
  ${FT8_ROOT}/fft/kiss_fftr.c
  oscarwatch_ft8.c
)

add_library(oscarwatch_ft8 SHARED ${FT8_SOURCES})
target_include_directories(oscarwatch_ft8 PRIVATE
  ${CMAKE_CURRENT_SOURCE_DIR}
  ${FT8_ROOT}
  ${FT8_ROOT}/ft8
  ${FT8_ROOT}/common
  ${FT8_ROOT}/fft
)
target_compile_definitions(oscarwatch_ft8 PRIVATE OSCARWATCH_FT8_EXPORTS)
if(UNIX)
  target_link_libraries(oscarwatch_ft8 PRIVATE m pthread)
endif()

# Fused multiply-add only. Not /fp:fast or -ffast-math: guards such as
# !(det > 1e-6) rely on IEEE comparisons, which those modes may rewrite.
if(MSVC)
  target_compile_options(oscarwatch_ft8 PRIVATE /O2 /fp:contract)
  target_link_options(oscarwatch_ft8 PRIVATE /LTCG)
else()
  # Always optimise the shipped native decode library (WSL MinGW and CI Release builds).
  target_compile_options(oscarwatch_ft8 PRIVATE -O3 -flto -ffp-contract=fast)
  target_link_options(oscarwatch_ft8 PRIVATE -flto)
  # Static-link libgcc so operators need no extra MinGW runtime DLL.
  if(CMAKE_C_COMPILER MATCHES "mingw" OR WIN32)
    target_link_options(oscarwatch_ft8 PRIVATE -static-libgcc)
  endif()
endif()

# Output name without lib prefix quirks for P/Invoke discovery
set_target_properties(oscarwatch_ft8 PROPERTIES
  OUTPUT_NAME "oscarwatch_ft8"
  PREFIX ""
)

install(TARGETS oscarwatch_ft8 RUNTIME DESTINATION bin LIBRARY DESTINATION lib)

# Decoder bench with per-stage timing. Off by default so CI builds only the library.
option(OW_FT8_BUILD_BENCH "Build the FT4 decode bench" OFF)
if(OW_FT8_BUILD_BENCH)
  add_executable(oscarwatch_ft8_bench ${FT8_SOURCES} bench.c)
  target_include_directories(oscarwatch_ft8_bench PRIVATE
    ${CMAKE_CURRENT_SOURCE_DIR}
    ${FT8_ROOT}
    ${FT8_ROOT}/ft8
    ${FT8_ROOT}/common
    ${FT8_ROOT}/fft
  )
  target_compile_definitions(oscarwatch_ft8_bench PRIVATE OSCARWATCH_FT8_EXPORTS OW_FT8_PROFILE)
  if(UNIX)
    target_link_libraries(oscarwatch_ft8_bench PRIVATE m pthread)
  endif()
  if(MSVC)
    target_compile_options(oscarwatch_ft8_bench PRIVATE /O2 /fp:contract)
  else()
    target_compile_options(oscarwatch_ft8_bench PRIVATE -O3 -ffp-contract=fast)
  endif()
endif()
