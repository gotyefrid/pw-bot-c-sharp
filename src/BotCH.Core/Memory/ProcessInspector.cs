using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace BotCH.Core.Memory;

/// <summary>
/// Диагностика процесса игры, только чтение: сколько свободно адресного пространства (проверка памяти перед и во время
/// работы бота), карта памяти, PEB, какой файл отображён по адресу (Probe memmap).
/// </summary>
public sealed class ProcessInspector(GameProcess game)
{
    /// <summary>
    /// Сколько адресного пространства игры свободно (VirtualQueryEx): всего и самый большой кусок подряд.
    /// null — Windows не ответила.
    /// </summary>
    public FreeMemory? QueryFreeMemory()
    {
        long total = 0, largest = 0;
        var regions = 0;
        foreach (var region in Regions())
        {
            regions++;
            if (region.State == MemoryRegion.Free)
            {
                total += region.Size;
                largest = Math.Max(largest, region.Size);
            }
        }

        return regions == 0 ? null : new FreeMemory(total, largest);
    }

    /// <summary>Карта адресного пространства игры (VirtualQueryEx) — для разбора, куда уходит память.</summary>
    public IEnumerable<MemoryRegion> Regions()
    {
        long address = 0;
        var size = new IntPtr(Marshal.SizeOf(typeof(NativeMethods.MemoryBasicInformation)));
        var regions = 0;
        while (address < 0x1_0000_0000 && regions++ < 1_000_000)
        {
            if (NativeMethods.VirtualQueryEx(game.Handle, new IntPtr(unchecked((int)address)), out var info, size) == IntPtr.Zero)
                yield break;

            var regionSize = (long)unchecked((uint)info.RegionSize.ToInt32());
            if (regionSize == 0)
                yield break;

            var start = unchecked((uint)info.BaseAddress.ToInt32());
            yield return new MemoryRegion(start, unchecked((uint)info.AllocationBase.ToInt32()), regionSize,
                info.State, info.Type, info.Protect);
            address = start + regionSize;
        }
    }

    /// <summary>Адрес PEB игры (бот и игра 32-битные — это её 32-битный PEB); null — Windows не ответила.</summary>
    public uint? PebAddress()
    {
        var status = NativeMethods.NtQueryInformationProcess(game.Handle, 0, out var info,
            Marshal.SizeOf(typeof(NativeMethods.ProcessBasicInformation)), out _);
        return status == 0 ? unchecked((uint)info.PebBaseAddress.ToInt32()) : null;
    }

    /// <summary>Файл, отображённый в память по этому адресу (для MEM_MAPPED и MEM_IMAGE), или null.</summary>
    public string? MappedFileName(uint address)
    {
        var name = new System.Text.StringBuilder(1024);
        var length = NativeMethods.GetMappedFileName(game.Handle, GameProcess.ToPointer(address), name, name.Capacity);
        return length == 0 ? null : name.ToString();
    }
}
