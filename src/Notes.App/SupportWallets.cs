using System.Collections.Generic;

namespace Notes.App;

/// <summary>Кошелёк автора: сеть и адрес.</summary>
public sealed record SupportWallet(string Network, string Address);

/// <summary>Кошельки автора для поддержки — в шапке окна («Поддержать») и в README.</summary>
public static class SupportWallets
{
    public static IReadOnlyList<SupportWallet> All { get; } =
    [
        new("USDT (TRC20)", "TGzNQgim9aa4S6YAE5JWxjJkUrFwBoqj9A"),
        new("USDT (Solana)", "AHesFaaikkmddM1TqgH78y4iPg9udGcwhQdWSAf5SBhV"),
    ];
}
