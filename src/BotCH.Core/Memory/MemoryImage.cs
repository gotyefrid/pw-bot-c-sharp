using System;
using System.Collections.Generic;

namespace BotCH.Core.Memory;

/// <summary>
/// Память «на бумаге»: набор страниц по 4 КБ, как в настоящем процессе.
/// Нужна для тестов без игры и для дампов (снимок памяти клиента → файл → тест).
/// Страница, в которую ничего не записывали, недоступна — чтение из неё возвращает false, как у живого клиента.
/// </summary>
public sealed class MemoryImage : IMemory
{
    public const int PageSize = 0x1000;

    private readonly Dictionary<uint, byte[]> _pages = new();

    public bool TryRead(uint address, byte[] buffer, int count)
    {
        if (count < 0 || count > buffer.Length || (ulong)address + (ulong)count > 0x1_0000_0000UL)
            return false;

        for (var done = 0; done < count;)
        {
            var current = address + (uint)done;
            if (!_pages.TryGetValue(current / PageSize, out var page))
                return false;

            var inPage = (int)(current % PageSize);
            var chunk = Math.Min(count - done, PageSize - inPage);
            Buffer.BlockCopy(page, inPage, buffer, done, chunk);
            done += chunk;
        }

        return true;
    }

    /// <summary>Пишет байты, при необходимости «выделяя» страницы. Всегда успешно.</summary>
    public bool TryWrite(uint address, byte[] data)
    {
        if ((ulong)address + (ulong)data.Length > 0x1_0000_0000UL)
            return false;

        for (var done = 0; done < data.Length;)
        {
            var current = address + (uint)done;
            var pageIndex = current / PageSize;
            if (!_pages.TryGetValue(pageIndex, out var page))
                _pages[pageIndex] = page = new byte[PageSize];

            var inPage = (int)(current % PageSize);
            var chunk = Math.Min(data.Length - done, PageSize - inPage);
            Buffer.BlockCopy(data, done, page, inPage, chunk);
            done += chunk;
        }

        return true;
    }

    /// <summary>Номера доступных страниц (адрес страницы = номер * <see cref="PageSize"/>).</summary>
    public IEnumerable<uint> PageNumbers => _pages.Keys;

    public int PageCount => _pages.Count;

    internal byte[] Page(uint number) => _pages[number];

    internal void SetPage(uint number, byte[] page) => _pages[number] = page;

    /// <summary>Делает страницы диапазона доступными (заполнены нулями), ничего не меняя в уже записанных.</summary>
    public void Map(uint address, int size)
    {
        for (var page = address / PageSize; page <= (address + (uint)Math.Max(size, 1) - 1) / PageSize; page++)
        {
            if (!_pages.ContainsKey(page))
                _pages[page] = new byte[PageSize];
        }
    }
}
