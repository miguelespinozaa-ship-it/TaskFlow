using TaskFlow.Domain.Common;
using TaskFlow.Domain.Projects;
using TaskFlow.Domain.Repositories;

namespace TaskFlow.UnitTests.Domain;

public sealed class RepositoryNameTests
{
    [Theory]
    [InlineData("dotnet/efcore", "dotnet", "efcore")]
    [InlineData("  dotnet/efcore  ", "dotnet", "efcore")]
    [InlineData("https://github.com/dotnet/efcore", "dotnet", "efcore")]
    [InlineData("https://github.com/dotnet/efcore/", "dotnet", "efcore")]
    [InlineData("https://github.com/dotnet/efcore.git", "dotnet", "efcore")]
    [InlineData("https://github.com/dotnet/efcore/tree/main/src", "dotnet", "efcore")]
    [InlineData("HTTPS://GitHub.com/dotnet/efcore", "dotnet", "efcore")]
    [InlineData("github.com/dotnet/efcore", "dotnet", "efcore")]
    [InlineData("git@github.com:dotnet/efcore.git", "dotnet", "efcore")]
    [InlineData("my-org/my.repo_name-2", "my-org", "my.repo_name-2")]
    public void Acepta_las_formas_habituales(string input, string owner, string name)
    {
        RepositoryName.TryParse(input, out var result).ShouldBeTrue();

        result.ShouldBe(new RepositoryName(owner, name));
        result.FullName.ShouldBe($"{owner}/{name}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("solo-un-segmento")]
    [InlineData("a/b/c")]                                  // sin prefijo de GitHub solo vale owner/name
    [InlineData("https://gitlab.com/dotnet/efcore")]       // otro host
    [InlineData("https://github.com.evil.com/a/b")]
    [InlineData("https://evil.com/github.com/a/b")]
    [InlineData("-empieza-con-guion/repo")]
    [InlineData("owner/..")]
    [InlineData("owner/re po")]
    [InlineData("owner/repo?x=1")]
    [InlineData("own er/repo")]
    [InlineData("../../etc/passwd")]
    public void Rechaza_lo_que_no_es_un_repositorio_de_GitHub(string? input)
    {
        RepositoryName.TryParse(input, out _).ShouldBeFalse();
    }
}

public sealed class FolderSummaryTests
{
    [Fact]
    public void Agrupa_por_carpeta_a_dos_niveles_y_ordena_por_cantidad_de_cambios()
    {
        var folders = FolderSummary.From([
            new ChangedFile("src/Api/Controllers/Tasks.cs", 10, 0),
            new ChangedFile("src/Api/Program.cs", 1, 1),
            new ChangedFile("src/Domain/Task.cs", 50, 20),
            new ChangedFile("docs/intro.md", 2, 0),
            new ChangedFile("README.md", 1, 0),
        ]);

        folders.ShouldBe([
            new FolderChange("src/Domain", 1, 50, 20),
            new FolderChange("src/Api", 2, 11, 1),
            new FolderChange("docs", 1, 2, 0),
            new FolderChange("", 1, 1, 0),
        ]);
    }

    [Fact]
    public void Con_profundidad_1_agrupa_por_carpeta_raiz()
    {
        var folders = FolderSummary.From(
            [new ChangedFile("src/Api/a.cs", 1, 0), new ChangedFile("src/Domain/b.cs", 1, 0)], depth: 1);

        folders.ShouldHaveSingleItem().ShouldBe(new FolderChange("src", 2, 2, 0));
    }

    [Fact]
    public void Sin_archivos_no_hay_carpetas() => FolderSummary.From([]).ShouldBeEmpty();

    [Fact]
    public void A_igual_cantidad_de_cambios_el_orden_es_estable()
    {
        var folders = FolderSummary.From([new ChangedFile("b/x", 1, 0), new ChangedFile("a/x", 1, 0)]);

        folders.Select(f => f.Path).ShouldBe(["a", "b"]);
    }
}

public sealed class RepositoryCommitTests
{
    private static readonly DateTime Now = new(2026, 9, 30, 12, 0, 0, DateTimeKind.Utc);
    private const string Sha = "0123456789ABCDEF0123456789abcdef01234567";

    private static RepositoryLink NewLink() =>
        RepositoryLink.Create(Project.Create(Guid.NewGuid(), "P", "PR", null, Now), new RepositoryName("acme", "web"), "main", Now);

    [Fact]
    public void El_commit_hereda_tenant_y_proyecto_del_enlace_y_totaliza_los_archivos()
    {
        var link = NewLink();

        var commit = RepositoryCommit.Create(link, Sha, "msg", "Ana", "ana", null, Now,
            [new ChangedFile("src/a.cs", 10, 2), new ChangedFile("src/b.cs", 5, 1)]);

        commit.WorkspaceId.ShouldBe(link.WorkspaceId);
        commit.ProjectId.ShouldBe(link.ProjectId);
        commit.Sha.ShouldBe(Sha.ToLowerInvariant());
        (commit.FilesChanged, commit.Additions, commit.Deletions).ShouldBe((2, 15, 3));
        commit.Folders.ShouldHaveSingleItem().Path.ShouldBe("src");
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzzz")]
    public void Sha_invalido_falla(string sha) =>
        Should.Throw<DomainException>(() => RepositoryCommit.Create(NewLink(), sha, "m", "A", null, null, Now, []));

    [Fact]
    public void Mensajes_enormes_se_truncan_y_autor_vacio_tiene_un_nombre()
    {
        var commit = RepositoryCommit.Create(NewLink(), Sha, new string('x', 20_000), "  ", null, null, Now, []);

        commit.Message.Length.ShouldBe(RepositoryCommit.MessageMaxLength);
        commit.AuthorName.ShouldBe("Desconocido");
    }

    [Fact]
    public void Un_commit_que_toca_cientos_de_carpetas_guarda_solo_las_principales()
    {
        var files = Enumerable.Range(0, 200).Select(i => new ChangedFile($"pkg{i}/file.cs", 200 - i, 0)).ToList();

        var commit = RepositoryCommit.Create(NewLink(), Sha, "m", "A", null, null, Now, files);

        commit.Folders.Count.ShouldBe(RepositoryCommit.MaxFolders);
        commit.Folders[0].Path.ShouldBe("pkg0");   // las de más cambios
        commit.FilesChanged.ShouldBe(200);         // el total sí cuenta todo
    }

    [Fact]
    public void MarkSynced_limpia_el_error_anterior()
    {
        var link = NewLink();
        link.MarkFailed(new string('e', 1000));
        link.LastSyncError!.Length.ShouldBe(RepositoryLink.ErrorMaxLength);

        link.MarkSynced(Now, "\"etag\"");

        link.LastSyncError.ShouldBeNull();
        link.LastSyncedAt.ShouldBe(Now);
        link.ETag.ShouldBe("\"etag\"");
    }
}
