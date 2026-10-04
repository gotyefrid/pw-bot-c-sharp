namespace BotCH.Core.Memory;

/// <summary>
/// Память чужого процесса на самом низком уровне: прочитать/записать кусок байтов.
/// Всё остальное (числа, строки, цепочки указателей) — в <see cref="MemoryExtensions"/>,
/// поэтому любая реализация (живой клиент, дамп для тестов) получает их бесплатно.
/// </summary>
public interface IMemory
{
    /// <summary>Читает <paramref name="count"/> байт в <paramref name="buffer"/>. false — адрес недоступен.</summary>
    bool TryRead(uint address, byte[] buffer, int count);

    /// <summary>Записывает байты. false — адрес недоступен или защищён.</summary>
    bool TryWrite(uint address, byte[] data);
}
