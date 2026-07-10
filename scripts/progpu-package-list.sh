#!/usr/bin/env bash

# shellcheck disable=SC2034 # This file is sourced by the pack and publish scripts.
progpu_avalonia_package_ids=(
  "ProGPU.Avalonia.Rendering"
  "ProGPU.Avalonia.SilkNet"
)

progpu_avalonia_package_projects=(
  "src/ProGpu/Avalonia.ProGpu/Avalonia.ProGpu.csproj"
  "src/Windows/Avalonia.SilkNet/Avalonia.SilkNet.csproj"
)
