# 临时CMakeLists，仅供本地调试用
cmake_minimum_required(VERSION 3.5)

project(THUAI9_CAPI VERSION 1.0)

set(CMAKE_CXX_STANDARD 17)
set(CMAKE_CXX_STANDARD_REQUIRED ON)
set(CMAKE_CXX_EXTENSIONS OFF)

if(MSVC)
    set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")
    foreach(flag
        CMAKE_C_FLAGS_DEBUG
        CMAKE_CXX_FLAGS_DEBUG
        CMAKE_C_FLAGS_RELEASE
        CMAKE_CXX_FLAGS_RELEASE
        CMAKE_C_FLAGS_RELWITHDEBINFO
        CMAKE_CXX_FLAGS_RELWITHDEBINFO
        CMAKE_C_FLAGS_MINSIZEREL
        CMAKE_CXX_FLAGS_MINSIZEREL)
        if(DEFINED ${flag})
            string(REPLACE "/MDd" "/MTd" ${flag} "${${flag}}")
            string(REPLACE "/MD" "/MT" ${flag} "${${flag}}")
        endif()
    endforeach()
    add_compile_options(/utf-8)
else()
    add_compile_options(-O2 -pthread -Wall -Wextra -Wpedantic)
endif()

aux_source_directory(./API/src CPP_LIST)
aux_source_directory(./proto PROTO_CPP_LIST)

find_package(Protobuf CONFIG REQUIRED)
find_package(gRPC CONFIG REQUIRED)

message(STATUS "Using protobuf ${Protobuf_VERSION}")
message(STATUS "Using gRPC ${gRPC_VERSION}")

add_executable(capi ${CPP_LIST} ${PROTO_CPP_LIST})

target_include_directories(capi PUBLIC ${PROJECT_SOURCE_DIR}/proto ${PROJECT_SOURCE_DIR}/API/include ${PROJECT_SOURCE_DIR}/tclap/include ${PROJECT_SOURCE_DIR}/spdlog/include ${PROJECT_SOURCE_DIR}/fmt)

target_link_libraries(capi
    protobuf::libprotobuf
    gRPC::grpc
    gRPC::grpc++_reflection
    gRPC::grpc++
)
