using System.Buffers.Binary;
using System.Buffers.Text;

namespace TaskFlow.Application.Common.Paging;

/// <summary>Página de resultados con keyset pagination. <see cref="NextCursor"/> null = no hay más.</summary>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor);

/// <summary>
/// Cursor opaco = (created_at, id) del último elemento de la página. La siguiente página es
/// "WHERE (created_at, id) &lt; (cursor)", que usa el índice: el coste es el mismo en la página 1 que en la 1000.
/// Con OFFSET, la página 1000 obliga a Postgres a leer y descartar todas las filas anteriores.
/// </summary>
public readonly record struct Cursor(DateTime CreatedAt, Guid Id)
{
    public const int MaxPageSize = 100;

    public string Encode()
    {
        Span<byte> buffer = stackalloc byte[24];
        BinaryPrimitives.WriteInt64BigEndian(buffer, CreatedAt.Ticks);
        Id.TryWriteBytes(buffer[8..]);
        return Base64Url.EncodeToString(buffer);
    }

    public static Cursor? Decode(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return null;

        Span<byte> buffer = stackalloc byte[24];
        int written;
        try
        {
            // Ojo: "Try" solo cubre que el buffer alcance; con caracteres inválidos lanza FormatException.
            if (!Base64Url.TryDecodeFromChars(value, buffer, out written) || written != 24)
                throw Validation.Fail("cursor", "Cursor inválido.");
        }
        catch (FormatException)
        {
            throw Validation.Fail("cursor", "Cursor inválido.");
        }

        var ticks = BinaryPrimitives.ReadInt64BigEndian(buffer);
        if (ticks < DateTime.MinValue.Ticks || ticks > DateTime.MaxValue.Ticks)
            throw Validation.Fail("cursor", "Cursor inválido.");

        return new Cursor(new DateTime(ticks, DateTimeKind.Utc), new Guid(buffer[8..]));
    }

    public static int ClampPageSize(int? pageSize) => Math.Clamp(pageSize ?? 20, 1, MaxPageSize);
}
