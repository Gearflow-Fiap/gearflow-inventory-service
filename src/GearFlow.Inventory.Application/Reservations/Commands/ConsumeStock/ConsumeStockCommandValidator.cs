using FluentValidation;

namespace GearFlow.Inventory.Application.Reservations.Commands.ConsumeStock;

internal sealed class ConsumeStockCommandValidator : AbstractValidator<ConsumeStockCommand>
{
    public ConsumeStockCommandValidator()
    {
        RuleFor(x => x.ServiceOrderId)
            .NotEmpty().WithMessage("O id da ordem de serviço é obrigatório.");
    }
}
