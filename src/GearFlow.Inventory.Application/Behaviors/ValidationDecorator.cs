using FluentValidation;
using GearFlow.Inventory.Application.Abstractions;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Behaviors;

/// <summary>
/// Roda os validators (FluentValidation) do command antes do handler. Falha de validação vira
/// <c>Result</c> de erro <c>Validation.Failed</c> (HTTP 400); o handler nem é chamado.
/// Command sem validator registrado passa direto.
/// </summary>
public sealed class ValidationDecorator<TCommand, TResult>(
    ICommandHandler<TCommand, TResult> inner,
    IEnumerable<IValidator<TCommand>> validators)
    : ICommandHandler<TCommand, TResult>
    where TCommand : ICommand<TResult>
    where TResult : Result
{
    public async Task<TResult> HandleAsync(TCommand command, CancellationToken ct)
    {
        var messages = new List<string>();
        foreach (var validator in validators)
        {
            var validation = await validator.ValidateAsync(command, ct);
            messages.AddRange(validation.Errors.Select(e => e.ErrorMessage));
        }

        if (messages.Count > 0)
            return ResultFactory<TResult>.Failure(
                Error.Validation("Validation.Failed", string.Join("; ", messages.Distinct())));

        return await inner.HandleAsync(command, ct);
    }
}
