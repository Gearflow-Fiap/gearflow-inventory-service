using FluentValidation;

namespace GearFlow.Inventory.Application.Reservations.Commands.ReserveStock;

internal sealed class ReserveStockCommandValidator : AbstractValidator<ReserveStockCommand>
{
    public ReserveStockCommandValidator()
    {
        RuleFor(x => x.ServiceOrderId)
            .NotEmpty().WithMessage("O id da ordem de serviço é obrigatório.");

        RuleFor(x => x.Items)
            .NotEmpty().WithMessage("A reserva precisa de pelo menos um item.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ItemId).NotEmpty().WithMessage("O id do item é obrigatório.");
            item.RuleFor(i => i.ItemType).IsInEnum().WithMessage("Tipo de item inválido.");
            item.RuleFor(i => i.Quantity).GreaterThan(0).WithMessage("A quantidade reservada deve ser maior que zero.");
            item.RuleFor(i => i.Quantity)
                .Must(q => q == decimal.Truncate(q))
                .When(i => i.ItemType == GearFlow.Contracts.InventoryItemType.Part)
                .WithMessage("A quantidade de peças deve ser um número inteiro.");
        });

        RuleFor(x => x.Items)
            .Must(items => items.Select(i => (i.ItemType, i.ItemId)).Distinct().Count() == items.Count)
            .When(x => x.Items is { Count: > 0 })
            .WithMessage("Há itens repetidos na reserva.");
    }
}
