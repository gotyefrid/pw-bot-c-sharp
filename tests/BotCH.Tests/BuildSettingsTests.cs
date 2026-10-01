using System;
using Xunit;

namespace BotCH.Tests;

public class BuildSettingsTests
{
    // Клиент игры 32-битный: если тесты (и бот) вдруг соберутся под x64, чтение указателей сломается
    [Fact]
    public void ProcessIs32Bit()
    {
        Assert.False(Environment.Is64BitProcess);
        Assert.Equal(4, IntPtr.Size);
    }
}
