using System;
using System.Numerics;
using Avalonia.Metadata;
using ProGPU.Backend;
using ProGPU.Scene;

namespace Avalonia.ProGpu;

/// <summary>
/// Provides scoped access to the active ProGPU drawing API.
/// </summary>
[Unstable]
public interface IProGpuApiLeaseFeature
{
    /// <summary>
    /// Leases the active ProGPU drawing API for the current render operation.
    /// </summary>
    IProGpuApiLease Lease();
}

/// <summary>
/// Provides exclusive, scoped access to ProGPU drawing and WebGPU resources.
/// </summary>
/// <remarks>
/// The lease must be disposed on the thread that acquired it before the custom render operation returns.
/// Objects exposed by the lease must not be retained after disposal.
/// </remarks>
[Unstable]
public interface IProGpuApiLease : IDisposable
{
    /// <summary>
    /// Gets the ProGPU command recorder for the active Avalonia drawing context.
    /// </summary>
    DrawingContext DrawingContext { get; }

    /// <summary>
    /// Gets the active ProGPU WebGPU context.
    /// </summary>
    WgpuContext WgpuContext { get; }

    /// <summary>
    /// Gets the transform that ProGPU drawing commands should apply to local coordinates.
    /// </summary>
    Matrix4x4 CurrentTransform { get; }

    /// <summary>
    /// Gets the effective Avalonia opacity at the lease point.
    /// </summary>
    double CurrentOpacity { get; }

    /// <summary>
    /// Gets the pixel size of the current render target.
    /// </summary>
    PixelSize PixelSize { get; }

    /// <summary>
    /// Gets the DPI of the current render target.
    /// </summary>
    Avalonia.Vector Dpi { get; }
}
