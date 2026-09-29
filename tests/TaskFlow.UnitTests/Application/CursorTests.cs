using FluentValidation;
using TaskFlow.Application.Common.Paging;

namespace TaskFlow.UnitTests.Application;

public sealed class CursorTests
{
    [Fact]
    public void Encode_y_Decode_son_inversos()
    {
        var cursor = new Cursor(new DateTime(2026, 9, 29, 12, 34, 56, DateTimeKind.Utc).AddTicks(1234), Guid.CreateVersion7());

        Cursor.Decode(cursor.Encode()).ShouldBe(cursor);
    }

    [Fact]
    public void El_cursor_es_url_safe()
    {
        var encoded = new Cursor(DateTime.UtcNow, Guid.NewGuid()).Encode();

        encoded.ShouldNotContain("+");
        encoded.ShouldNotContain("/");
        encoded.ShouldNotContain("=");
    }

    [Theory]
    [InlineData("basura")]
    [InlineData("AAAA")]
    [InlineData("////////////////////////////////")]
    public void Cursor_invalido_es_error_de_validacion(string value) =>
        Should.Throw<ValidationException>(() => Cursor.Decode(value));

    [Fact]
    public void Cursor_vacio_es_primera_pagina() => Cursor.Decode(null).ShouldBeNull();

    [Theory]
    [InlineData(null, 20)]
    [InlineData(0, 1)]
    [InlineData(500, Cursor.MaxPageSize)]
    [InlineData(50, 50)]
    public void PageSize_se_acota(int? requested, int expected) => Cursor.ClampPageSize(requested).ShouldBe(expected);
}
