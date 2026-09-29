using FluentValidation;
using TaskFlow.Application.Abstractions;
using TaskFlow.Application.Common;
using TaskFlow.Application.Common.Exceptions;
using TaskFlow.Domain.Labels;

namespace TaskFlow.Application.Labels;

public sealed record LabelDto(Guid Id, string Name, string Color);

public sealed record SaveLabelRequest(string Name, string Color);

public sealed class SaveLabelRequestValidator : AbstractValidator<SaveLabelRequest>
{
    public SaveLabelRequestValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Label.NameMaxLength);
        RuleFor(x => x.Color).NotEmpty().Matches("^#[0-9a-fA-F]{6}$").WithMessage("Color hexadecimal, p. ej. #4f46e5.");
    }
}

public interface ILabelService
{
    Task<IReadOnlyList<LabelDto>> ListAsync(CancellationToken ct);
    Task<LabelDto> CreateAsync(SaveLabelRequest request, CancellationToken ct);
    Task<LabelDto> UpdateAsync(Guid id, SaveLabelRequest request, CancellationToken ct);
    Task DeleteAsync(Guid id, CancellationToken ct);
}

public sealed class LabelService(
    ILabelRepository labels,
    IUnitOfWork unitOfWork,
    ITenantContext tenant,
    IRequestValidator validator) : ILabelService
{
    public Task<IReadOnlyList<LabelDto>> ListAsync(CancellationToken ct) => labels.ListAsync(ct);

    public async Task<LabelDto> CreateAsync(SaveLabelRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        if (await labels.NameExistsAsync(request.Name.Trim(), exceptId: null, ct))
            throw new ConflictException("Ya existe una etiqueta con ese nombre.");

        var label = Label.Create(tenant.RequireWorkspaceId(), request.Name, request.Color);
        labels.Add(label);
        await unitOfWork.SaveChangesAsync(ct);
        return new LabelDto(label.Id, label.Name, label.Color);
    }

    public async Task<LabelDto> UpdateAsync(Guid id, SaveLabelRequest request, CancellationToken ct)
    {
        await validator.ValidateAsync(request, ct);
        var label = await labels.GetByIdAsync(id, ct) ?? throw new NotFoundException("Etiqueta no encontrada.");
        if (await labels.NameExistsAsync(request.Name.Trim(), exceptId: id, ct))
            throw new ConflictException("Ya existe una etiqueta con ese nombre.");

        label.Update(request.Name, request.Color);
        await unitOfWork.SaveChangesAsync(ct);
        return new LabelDto(label.Id, label.Name, label.Color);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var label = await labels.GetByIdAsync(id, ct) ?? throw new NotFoundException("Etiqueta no encontrada.");
        labels.Remove(label); // borrado físico: las filas de task_labels caen en cascada
        await unitOfWork.SaveChangesAsync(ct);
    }
}
