using System.Buffers.Binary;
using System.Collections.Immutable;

namespace NativeWebView.Core;

internal static class NativeWebViewStatusTextNormalizer
{
    internal const int MaximumLength = 2048;

    internal static string? Normalize(string? statusText)
    {
        if (statusText is null)
            return null;

        var normalized = statusText.AsSpan().Trim();
        if (normalized.IsEmpty)
            return null;
        if (normalized.Length > MaximumLength)
        {
            var truncatedLength = MaximumLength;
            if (char.IsHighSurrogate(normalized[truncatedLength - 1]) &&
                char.IsLowSurrogate(normalized[truncatedLength]))
            {
                truncatedLength--;
            }

            normalized = normalized[..truncatedLength];
        }
        return normalized.ToString();
    }
}

/// <summary>Contains a PNG snapshot of the visible embedded WebView viewport.</summary>
public sealed class NativeWebViewSnapshot
{
    private static ReadOnlySpan<byte> PngSignature =>
        [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
    private static readonly ImmutableArray<uint> Crc32Table = CreateCrc32Table();

    private readonly ImmutableArray<byte> _pngData;

    /// <summary>Initializes a snapshot by copying the supplied PNG data.</summary>
    /// <param name="pngData">A non-empty PNG image.</param>
    public NativeWebViewSnapshot(byte[] pngData)
    {
        ArgumentNullException.ThrowIfNull(pngData);
        if (!IsValidPng(pngData))
            throw new ArgumentException("Snapshot data must contain a structurally valid PNG image.", nameof(pngData));

        _pngData = ImmutableArray.CreateRange(pngData);
    }

    /// <summary>Gets the snapshot MIME type.</summary>
    public string ContentType => "image/png";

    /// <summary>Gets immutable PNG data owned by this snapshot.</summary>
    public ImmutableArray<byte> PngData => _pngData;

    private static bool IsValidPng(ReadOnlySpan<byte> pngData)
    {
        if (pngData.Length < 45 || !pngData[..PngSignature.Length].SequenceEqual(PngSignature))
            return false;

        var offset = PngSignature.Length;
        var chunkIndex = 0;
        var sawImageData = false;
        var imageDataEnded = false;
        var requiresPalette = false;
        var sawPalette = false;
        var permitsPalette = false;
        long imageDataLength = 0;

        while (offset <= pngData.Length - 12)
        {
            var dataLengthValue = BinaryPrimitives.ReadUInt32BigEndian(pngData.Slice(offset, 4));
            if (dataLengthValue > int.MaxValue)
                return false;

            var dataLength = (int)dataLengthValue;
            var chunkLength = 12L + dataLength;
            if (chunkLength > pngData.Length - offset)
                return false;

            var chunkType = pngData.Slice(offset + 4, 4);
            if (!IsValidChunkType(chunkType))
                return false;

            var chunkData = pngData.Slice(offset + 8, dataLength);
            var expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(pngData.Slice(offset + 8 + dataLength, 4));
            if (ComputeCrc32(chunkType, chunkData) != expectedCrc)
                return false;

            if (chunkIndex == 0)
            {
                if (!chunkType.SequenceEqual("IHDR"u8) ||
                    !IsValidHeader(chunkData, out requiresPalette, out permitsPalette))
                    return false;
            }
            else if (chunkType.SequenceEqual("IHDR"u8))
            {
                return false;
            }
            else if (chunkType.SequenceEqual("PLTE"u8))
            {
                if (!permitsPalette || sawImageData || sawPalette || dataLength == 0 || dataLength % 3 != 0 || dataLength > 768)
                    return false;
                sawPalette = true;
            }
            else if (chunkType.SequenceEqual("IDAT"u8))
            {
                if (imageDataEnded || (requiresPalette && !sawPalette))
                    return false;
                sawImageData = true;
                imageDataLength += dataLength;
            }
            else
            {
                if (sawImageData)
                    imageDataEnded = true;

                if (chunkType.SequenceEqual("IEND"u8))
                    return dataLength == 0 &&
                           sawImageData &&
                           imageDataLength > 0 &&
                           offset + chunkLength == pngData.Length;

                if (IsCriticalChunk(chunkType))
                    return false;
            }

            offset += (int)chunkLength;
            chunkIndex++;
        }

        return false;
    }

    private static bool IsValidHeader(
        ReadOnlySpan<byte> header,
        out bool requiresPalette,
        out bool permitsPalette)
    {
        requiresPalette = false;
        permitsPalette = false;
        if (header.Length != 13 ||
            BinaryPrimitives.ReadUInt32BigEndian(header[..4]) == 0 ||
            BinaryPrimitives.ReadUInt32BigEndian(header.Slice(4, 4)) == 0 ||
            header[10] != 0 ||
            header[11] != 0 ||
            header[12] > 1)
        {
            return false;
        }

        var bitDepth = header[8];
        var colorType = header[9];
        requiresPalette = colorType == 3;
        permitsPalette = colorType is 2 or 3 or 6;
        return colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            2 => bitDepth is 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            4 => bitDepth is 8 or 16,
            6 => bitDepth is 8 or 16,
            _ => false,
        };
    }

    private static bool IsCriticalChunk(ReadOnlySpan<byte> chunkType) =>
        chunkType.Length == 4 && (chunkType[0] & 0x20) == 0;

    private static bool IsValidChunkType(ReadOnlySpan<byte> chunkType)
    {
        if (chunkType.Length != 4 || (chunkType[2] & 0x20) != 0)
            return false;

        foreach (var value in chunkType)
        {
            if (value is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z'))
                return false;
        }

        return true;
    }

    private static uint ComputeCrc32(ReadOnlySpan<byte> chunkType, ReadOnlySpan<byte> chunkData)
    {
        var crc = uint.MaxValue;
        crc = AppendCrc32(crc, chunkType);
        crc = AppendCrc32(crc, chunkData);
        return ~crc;
    }

    private static uint AppendCrc32(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var value in data)
            crc = Crc32Table[(byte)(crc ^ value)] ^ (crc >> 8);
        return crc;
    }

    private static ImmutableArray<uint> CreateCrc32Table()
    {
        var table = ImmutableArray.CreateBuilder<uint>(256);
        for (uint index = 0; index < 256; index++)
        {
            var value = index;
            for (var bit = 0; bit < 8; bit++)
                value = (value >> 1) ^ (0xedb88320U & (uint)-(int)(value & 1));
            table.Add(value);
        }

        return table.MoveToImmutable();
    }
}

/// <summary>Represents an embedded snapshot operation and its native registration milestone.</summary>
public sealed class NativeWebViewSnapshotCapture
{
    /// <summary>Initializes a snapshot capture operation.</summary>
    /// <param name="captureStarted">
    /// Completes after native capture registration has either completed or failed, after which an embedded
    /// native surface may be hidden without racing registration.
    /// </param>
    /// <param name="completion">Completes with the captured snapshot, or <see langword="null"/> when unavailable.</param>
    public NativeWebViewSnapshotCapture(Task captureStarted, Task<NativeWebViewSnapshot?> completion)
    {
        CaptureStarted = captureStarted ?? throw new ArgumentNullException(nameof(captureStarted));
        Completion = completion ?? throw new ArgumentNullException(nameof(completion));
    }

    /// <summary>Gets the native capture-registration milestone.</summary>
    public Task CaptureStarted { get; }

    /// <summary>Gets the snapshot completion.</summary>
    public Task<NativeWebViewSnapshot?> Completion { get; }

    /// <summary>Creates a capture that has already completed.</summary>
    /// <param name="snapshot">The completed snapshot result.</param>
    public static NativeWebViewSnapshotCapture FromResult(NativeWebViewSnapshot? snapshot) =>
        new(Task.CompletedTask, Task.FromResult(snapshot));
}

/// <summary>Captures the visible viewport of an embedded native WebView.</summary>
public interface INativeWebViewSnapshotProvider
{
    /// <summary>Begins a snapshot and exposes when native registration has completed.</summary>
    /// <remarks>
    /// The default implementation conservatively completes the registration milestone with the snapshot operation.
    /// Providers that can identify an earlier native-registration boundary should override this method.
    /// </remarks>
    NativeWebViewSnapshotCapture BeginCaptureSnapshot(CancellationToken cancellationToken = default)
    {
        Task<NativeWebViewSnapshot?> completion;
        try
        {
            completion = CaptureSnapshotAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            completion = Task.FromCanceled<NativeWebViewSnapshot?>(cancellationToken);
        }
        catch (Exception exception)
        {
            completion = Task.FromException<NativeWebViewSnapshot?>(exception);
        }

        var normalizedCompletion = NormalizeCompletionAsync(completion, cancellationToken);
        return new NativeWebViewSnapshotCapture(normalizedCompletion, normalizedCompletion);
    }

    /// <summary>
    /// Captures the current viewport, returning <see langword="null"/> when a capture is temporarily unavailable.
    /// </summary>
    Task<NativeWebViewSnapshot?> CaptureSnapshotAsync(CancellationToken cancellationToken = default);

    private static async Task<NativeWebViewSnapshot?> NormalizeCompletionAsync(
        Task<NativeWebViewSnapshot?> completion,
        CancellationToken cancellationToken)
    {
        try
        {
            return await completion.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Provides data for a native WebView status-text change.</summary>
public sealed class NativeWebViewStatusTextChangedEventArgs : EventArgs
{
    /// <summary>Initializes the event data.</summary>
    public NativeWebViewStatusTextChangedEventArgs(string? statusText)
    {
        StatusText = statusText;
    }

    /// <summary>Gets the current status text, or <see langword="null"/> when no status is available.</summary>
    /// <remarks>Unusually long page-controlled values may be truncated.</remarks>
    public string? StatusText { get; }
}

/// <summary>Reports text normally displayed by a browser status UI, such as a hovered link target.</summary>
public interface INativeWebViewStatusTextProvider
{
    /// <summary>Gets the current status text.</summary>
    /// <remarks>Unusually long page-controlled values may be truncated.</remarks>
    string? StatusText { get; }

    /// <summary>Occurs when <see cref="StatusText"/> changes.</summary>
    event EventHandler<NativeWebViewStatusTextChangedEventArgs>? StatusTextChanged;
}
