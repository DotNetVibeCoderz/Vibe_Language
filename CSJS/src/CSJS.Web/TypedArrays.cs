namespace CSJS.Web;

// The ES typed arrays. They are JavaScript built-ins rather than web platform interfaces, so
// lib.dom only refers to them; the platform uses them everywhere — canvas pixels, audio samples,
// WebSocket frames, crypto digests — so they are declared here, by hand, beside the generated file.

/// <summary>A fixed-length block of raw bytes.</summary>
[JsGlobal("ArrayBuffer")]
public class ArrayBuffer
{
    public ArrayBuffer(int byteLength) => throw new JsExternException();

    public int ByteLength => throw new JsExternException();
    public ArrayBuffer Slice(int begin) => throw new JsExternException();
    public ArrayBuffer Slice(int begin, int end) => throw new JsExternException();

    public static bool IsView(object value) => throw new JsExternException();
}

/// <summary>What every typed array view shares. Not constructible on its own.</summary>
[JsGlobal("Object")]
public abstract class TypedArray
{
    public ArrayBuffer Buffer => throw new JsExternException();
    public int ByteLength => throw new JsExternException();
    public int ByteOffset => throw new JsExternException();
    public int Length => throw new JsExternException();
}

/// <summary>Bytes, 0–255. What <c>fetch</c> bodies, WebSocket frames, and digests are made of.</summary>
[JsGlobal("Uint8Array")]
public class Uint8Array : TypedArray
{
    public Uint8Array(int length) => throw new JsExternException();
    public Uint8Array(ArrayBuffer buffer) => throw new JsExternException();
    public Uint8Array(ArrayBuffer buffer, int byteOffset, int length) => throw new JsExternException();
    public Uint8Array(int[] values) => throw new JsExternException();
    public Uint8Array(byte[] values) => throw new JsExternException();

    public int this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }

    public void Set(Uint8Array source, int offset) => throw new JsExternException();
    public void Set(int[] source, int offset) => throw new JsExternException();
    public Uint8Array Subarray(int begin, int end) => throw new JsExternException();
    public Uint8Array Slice(int begin, int end) => throw new JsExternException();
    public Uint8Array Fill(int value) => throw new JsExternException();
    public int IndexOf(int value) => throw new JsExternException();
    public bool Includes(int value) => throw new JsExternException();

    /// <summary>A plain array of the values, for LINQ and <c>List&lt;int&gt;</c>.</summary>
    [JsTemplate("Array.from({this})")]
    public int[] ToArray() => throw new JsExternException();
}

/// <summary>Bytes that clamp instead of wrapping: canvas <c>ImageData</c> pixels.</summary>
[JsGlobal("Uint8ClampedArray")]
public class Uint8ClampedArray : TypedArray
{
    public Uint8ClampedArray(int length) => throw new JsExternException();
    public Uint8ClampedArray(ArrayBuffer buffer) => throw new JsExternException();
    public Uint8ClampedArray(int[] values) => throw new JsExternException();

    public int this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }

    public void Set(Uint8ClampedArray source, int offset) => throw new JsExternException();
    public Uint8ClampedArray Fill(int value) => throw new JsExternException();

    [JsTemplate("Array.from({this})")]
    public int[] ToArray() => throw new JsExternException();
}

[JsGlobal("Int8Array")]
public class Int8Array : TypedArray
{
    public Int8Array(int length) => throw new JsExternException();
    public Int8Array(ArrayBuffer buffer) => throw new JsExternException();
    public int this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }
}

[JsGlobal("Uint16Array")]
public class Uint16Array : TypedArray
{
    public Uint16Array(int length) => throw new JsExternException();
    public Uint16Array(ArrayBuffer buffer) => throw new JsExternException();
    public int this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }
}

[JsGlobal("Int16Array")]
public class Int16Array : TypedArray
{
    public Int16Array(int length) => throw new JsExternException();
    public Int16Array(ArrayBuffer buffer) => throw new JsExternException();
    public int this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }
}

[JsGlobal("Uint32Array")]
public class Uint32Array : TypedArray
{
    public Uint32Array(int length) => throw new JsExternException();
    public Uint32Array(ArrayBuffer buffer) => throw new JsExternException();
    public uint this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }
}

[JsGlobal("Int32Array")]
public class Int32Array : TypedArray
{
    public Int32Array(int length) => throw new JsExternException();
    public Int32Array(ArrayBuffer buffer) => throw new JsExternException();
    public int this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }
}

/// <summary>32-bit floats: Web Audio samples and analyser data, WebGL buffers.</summary>
[JsGlobal("Float32Array")]
public class Float32Array : TypedArray
{
    public Float32Array(int length) => throw new JsExternException();
    public Float32Array(ArrayBuffer buffer) => throw new JsExternException();
    public Float32Array(double[] values) => throw new JsExternException();
    public double this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }

    public void Set(Float32Array source, int offset) => throw new JsExternException();
    public Float32Array Subarray(int begin, int end) => throw new JsExternException();

    [JsTemplate("Array.from({this})")]
    public double[] ToArray() => throw new JsExternException();
}

[JsGlobal("Float64Array")]
public class Float64Array : TypedArray
{
    public Float64Array(int length) => throw new JsExternException();
    public Float64Array(ArrayBuffer buffer) => throw new JsExternException();
    public Float64Array(double[] values) => throw new JsExternException();
    public double this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }

    [JsTemplate("Array.from({this})")]
    public double[] ToArray() => throw new JsExternException();
}

[JsGlobal("BigInt64Array")]
public class BigInt64Array : TypedArray
{
    public BigInt64Array(int length) => throw new JsExternException();
    public long this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }
}

[JsGlobal("BigUint64Array")]
public class BigUint64Array : TypedArray
{
    public BigUint64Array(int length) => throw new JsExternException();
    public ulong this[int index] { get => throw new JsExternException(); set => throw new JsExternException(); }
}

/// <summary>Reads and writes mixed-width numbers at byte offsets — binary protocols, file headers.</summary>
[JsGlobal("DataView")]
public class DataView
{
    public DataView(ArrayBuffer buffer) => throw new JsExternException();
    public DataView(ArrayBuffer buffer, int byteOffset, int byteLength) => throw new JsExternException();

    public ArrayBuffer Buffer => throw new JsExternException();
    public int ByteLength => throw new JsExternException();
    public int ByteOffset => throw new JsExternException();

    public int GetUint8(int byteOffset) => throw new JsExternException();
    public int GetInt8(int byteOffset) => throw new JsExternException();
    public int GetUint16(int byteOffset, bool littleEndian = false) => throw new JsExternException();
    public int GetInt16(int byteOffset, bool littleEndian = false) => throw new JsExternException();
    public uint GetUint32(int byteOffset, bool littleEndian = false) => throw new JsExternException();
    public int GetInt32(int byteOffset, bool littleEndian = false) => throw new JsExternException();
    public double GetFloat32(int byteOffset, bool littleEndian = false) => throw new JsExternException();
    public double GetFloat64(int byteOffset, bool littleEndian = false) => throw new JsExternException();

    public void SetUint8(int byteOffset, int value) => throw new JsExternException();
    public void SetInt8(int byteOffset, int value) => throw new JsExternException();
    public void SetUint16(int byteOffset, int value, bool littleEndian = false) => throw new JsExternException();
    public void SetInt16(int byteOffset, int value, bool littleEndian = false) => throw new JsExternException();
    public void SetUint32(int byteOffset, uint value, bool littleEndian = false) => throw new JsExternException();
    public void SetInt32(int byteOffset, int value, bool littleEndian = false) => throw new JsExternException();
    public void SetFloat32(int byteOffset, double value, bool littleEndian = false) => throw new JsExternException();
    public void SetFloat64(int byteOffset, double value, bool littleEndian = false) => throw new JsExternException();
}
