{
  "targets": [
    {
      "target_name": "gns",
      "sources": [ "src/addon.cpp" ],
      "include_dirs": [
        "<!@(node -p \"require('node-addon-api').include\")",
        "deps/include"
      ],
      "defines": [ "NAPI_DISABLE_CPP_EXCEPTIONS=0", "NAPI_VERSION=8" ],
      "cflags!": [ "-fno-exceptions" ],
      "cflags_cc!": [ "-fno-exceptions" ],
      "conditions": [
        [ "OS=='win'", {
          "libraries": [ "<(module_root_dir)/native/win-x64/GameNetworkingSockets.lib" ],
          "copies": [ {
            "destination": "<(module_root_dir)/build/Release/",
            "files": [ "<(module_root_dir)/native/win-x64/GameNetworkingSockets.dll" ]
          } ],
          "msvs_settings": {
            "VCCLCompilerTool": { "ExceptionHandling": 1, "AdditionalOptions": [ "/std:c++17" ] }
          }
        } ],
        [ "OS=='linux'", {
          "libraries": [
            "-L<(module_root_dir)/native/linux-x64",
            "-lGameNetworkingSockets",
            "-Wl,-rpath,'$$ORIGIN'"
          ],
          "cflags_cc": [ "-std=c++17" ],
          "copies": [ {
            "destination": "<(module_root_dir)/build/Release/",
            "files": [ "<(module_root_dir)/native/linux-x64/libGameNetworkingSockets.so" ]
          } ]
        } ],
        [ "OS=='mac'", {
          "libraries": [ "<(module_root_dir)/native/osx-x64/libGameNetworkingSockets.dylib" ],
          "xcode_settings": {
            "CLANG_CXX_LANGUAGE_STANDARD": "c++17",
            "GCC_ENABLE_CPP_EXCEPTIONS": "YES"
          }
        } ]
      ]
    }
  ]
}
