using FluentValidation;

namespace GearFlow.Inventory.Application.Reservations.Commands.ReleaseStock;

internal sealed class ReleaseStockCommandValidator : AbstractValidator<ReleaseStockCommand>
{
    public ReleaseStockCommandValidator()
    {
        RuleFor(x => x.ServiceOrderId)
            .NotEmpty().WithMessage("O id da ordem de serviço é obrigatório.");
    }
}
