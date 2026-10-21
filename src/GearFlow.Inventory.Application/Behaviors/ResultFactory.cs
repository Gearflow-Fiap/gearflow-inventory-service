using System.Reflection;
using GearFlow.Inventory.Domain.Core;

namespace GearFlow.Inventory.Application.Behaviors;

/// <summary>
/// Cria o resultado de falha do tipo certo (<see cref="Result"/> ou <see cref="Result{TValue}"/>) para os
/// decorators, que só conhecem o <typeparamref name="TResult"/> em tempo de compilação genérica.
/// </summary>
public static class ResultFactory<TResult> where TResult : Result
{
    private static readonly Func<Error, TResult> FailureFactory = CreateFailureFactory();

    public static TResult Failure(Error error) => FailureFactory(error);

    private static Func<Error, TResult> CreateFailureFactory()
    {
        if (typeof(TResult) == typeof(Result))
            return error => (TResult)Result.Failure(error);

        if (typeof(TResult).IsGenericType && typeof(TResult).GetGenericTypeDefinition() == typeof(Result<>))
        {
            var valueType = typeof(TResult).GetGenericArguments()[0];
            var failure = typeof(Result)
                .GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(m => m.Name == nameof(Result.Failure) && m.IsGenericMethodDefinition)
                .MakeGenericMethod(valueType);

            return error => (TResult)failure.Invoke(null, [error])!;
        }

        throw new NotSupportedException(
            $"{typeof(TResult)} não é suportado: handlers devem retornar Result ou Result<T>.");
    }
}
