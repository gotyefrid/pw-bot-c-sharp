using System.Collections.Generic;

namespace BotCH.MemoryHelpers.Offsets
{
    /* PwClassic 136
     https://pwclassic.net/

     Все смещения — от начала модуля ElementClient.exe (он грузится с 0x400000, т.е. адрес в IDA/x32dbg = 0x400000 + смещение).
     Значения со строкой-ключом можно переопределить в config.ini, секция [offsets].

     КАК ИСКАТЬ ПОЛЯ В ПАМЯТИ ДЛЯ ДРУГОГО КЛИЕНТА (метод «до/после»)
       1. Снять дамп структуры объекта (моба, предмета) — например, первые 0x600 байт.
       2. Сделать в игре одно действие (убить моба, натравить пета, бросить предмет).
       3. Снять дамп ещё раз и сравнить: искомое поле меняется ровно один раз и в нужный момент.
       4. Проверить кандидата на других объектах вокруг: у всех «обычных» должно быть одно и то же значение.
       Одного опыта мало: в первом тесте «флаг боя» легко принять за «флаг смерти». Повторять минимум дважды.
       HP моба для этого не годится: клиент знает HP только у выбранной цели, у остальных там 0.

     КАК ИСКАТЬ ФУНКЦИИ ОТПРАВКИ КОМАНД (c2s_SendCmd*) В ДРУГОМ КЛИЕНТЕ
       Каждая команда серверу — пакет [номер команды, 2 байта][данные]. Все функции устроены одинаково:
           выделить буфер; mov word [esi], НОМЕР; записать данные; push РАЗМЕР; push esi; call SendGameData
       1. SendGameData — функция, которую вызывают больше всего мест (здесь 0x5D2350, ~168 вызовов, ecx = [[0x9B3EEC]+0x20]).
          Ищется перебором exe: места «mov word [reg], маленькое число ... call X», самый частый X — она.
       2. Все её вызывающие лежат подряд одной таблицей (здесь ~0x5F01C0–0x5F3800) в порядке номеров команд.
       3. Нужную выбираем по номеру команды (из протокола PW) и проверяем по размеру пакета:
           0x02 выбор цели (6 байт), 0x03 обычная атака (3), 0x06 подбор (10), 0x08 снять цель, 0x28 предмет, 0x29 скилл.
          0x01 — выход из игры, НЕ вызывать.
       4. Проверить в игре тестовым вызовом. Код игры не менять: патч .text защита ловит и закрывает клиент.
       Сигнатуры *_SIG — первые байты функции; если не совпали, GameCall вызов не делает (значит, клиент другой).
    */
    public class PwClassic136 : OffsetTemplate
    {
        // Базовый указатель игры. Тот же, что использует SendGameData: [0x9B3EEC] = [0x400000 + 0x5B3EEC]
        public override uint BASEADDR_OFFSET { get => GetOffsetFromIni("baseAddress", "0x5B3EEC"); }
        public override uint GAMEADDR_OFFSET { get => GetOffsetFromIni("gameAddr", "0x1C"); }
        public override uint UNZREEFE_OFFSET { get => GetOffsetFromIni("unfrz", "0x48C"); }
        public override uint SET_TARGET_FUNC_OFFSET { get; }
        public override byte[] ORIG_BYTES_FUNC_OFFSET => new byte[] { };
        public override uint PERS_STRUCT_OFFSET { get => GetOffsetFromIni("persStruct", "0x20"); }
        public override uint PERS_NAME { get => GetOffsetFromIni("persName", "0x608"); }
        public override uint PERS_FLAG_SKILL_OFFSET { get => GetOffsetFromIni("flagSkill", "0xB8"); }
        public override uint PERS_WID_OFFSET { get => GetOffsetFromIni("persWid", "0x458"); }
        public override uint PERS_HP_OFFSET { get => GetOffsetFromIni("persHp", "0x46C"); }
        public override uint PERS_MP_OFFSET { get => GetOffsetFromIni("persMp", "0x470"); }
        public override uint PERS_MAX_HP_OFFSET { get => GetOffsetFromIni("persMaxHp", "0x4A4"); }
        public override uint PERS_TARGETID_OFFSET { get => GetOffsetFromIni("persTargetId", "0xAF0"); }
        public override uint PERS_COOLDOWN_FEED_PET_OFFSET { get => GetOffsetFromIni("persCdFeedPet", "0xBF4"); } //
        public override uint PERS_COOLDOWN_POT_HP_OFFSET { get => GetOffsetFromIni("persCdPotHp", "0x9EC"); }
        public override uint PERS_LOC_X { get => 0x3C; }
        public override uint PERS_LOC_Z { get => 0x40; }
        public override uint PERS_LOC_Y { get => 0x44; }

        public override uint PET_STRUCT_OFFSET { get => GetOffsetFromIni("petStruct", "0xE60"); }
        public override Dictionary<int, uint> PET_CAGES_ARRAY
        {
            get => new Dictionary<int, uint>
            {
                    { 1, 0x10 },
                    { 2, 0x14 },
                    { 3, 0x18 },
                    { 4, 0x1C },
                    { 5, 0x20 },
            };
        }
        // WID вызванного пета. Он же лежит в списке мобов с типом 9 (TargetMobEntity.TYPE_PET)
        public override uint PET_CURRENT_PETID_OFFSET { get => GetOffsetFromIni("petCurrentPetId", "0x38"); }
        public override uint PET_HP_OFFSET { get => GetOffsetFromIni("petHp", "0x1C"); }
        public override uint PET_FEED_STATUS_OFFSET { get => GetOffsetFromIni("petFeedStatus", "0x8"); }

        // Список мобов/NPC/петов: [[game + MOB_OFFSET_1] + MOB_OFFSET_2] + MOB_STRUCT_OFFSET — массив 769 ячеек, в ячейке +0x4 — объект.
        // Рядом в том же «мире» (game + MOB_OFFSET_1) лежат другие менеджеры с таким же устройством, см. GROUND_ITEMS_OFFSET.
        public override uint MOB_OFFSET_1 { get => GetOffsetFromIni("mobOffset1", "0x8"); }
        public override uint MOB_OFFSET_2 { get => GetOffsetFromIni("mobOffset2", "0x24"); }
        public override uint MOB_NAME { get => 0x26C; }
        public override uint MOB_STRUCT_OFFSET { get => GetOffsetFromIni("mobStruct", "0x18"); }
        // Тип: 6 моб, 7 NPC, 9 пет (TargetMobEntity.TYPE_*)
        public override uint MOB_TYPE_OFFSET { get => GetOffsetFromIni("mobType", "0xB4"); }
        // Состояние моба (TargetMobEntity.ACTION_*): 1 стоит, 5 идёт, 2 бьёт физ. атакой, 3 кастует заклинание, 4 мёртв (держится, пока лежит труп).
        // Найдено «до/после» на убийстве моба: 4 ставится ровно в момент смерти. На Comeback это поле 0x2D4.
        public override uint MOB_ACTION_OFFSET { get => GetOffsetFromIni("mobAction", "0x2B8"); }
        // WID = константа + номер ячейки в списке, поэтому моб после респавна получает ТОТ ЖЕ WID. Чёрные списки по WID не работают.
        public override uint MOB_WID_OFFSET { get => GetOffsetFromIni("mobWid", "0x11C"); }
        // Только у выбранной цели; у остальных мобов 0. До 0 при смерти может и не дойти
        public override uint MOB_HP_OFFSET { get => 0x12C; }
        public override uint MOB_DIST_OFFSET { get => GetOffsetFromIni("mobDistance", "0x270"); }
        // Цель моба: WID персонажа или пета, на которого он агрится; 0 — моб нейтрален.
        // Найдено так: натравить пета на моба и искать в структуре моба поле, где появится WID пета (PERS_WID / PET_CURRENT_PETID).
        // Рядом (0x0DC: 0x96 -> 0x64) есть ещё флаг «моб вступил в бой» — не путать со смертью.
        public override uint MOB_TARGET_OFFSET { get => GetOffsetFromIni("mobTarget", "0x2D4"); }

        // c2s_SendCmdSelectTarget(int id), команда 0x02, пакет 6 байт. Код: push esi; push 6; call alloc ... mov word [esi], 2
        public override uint C2S_SELECT_TARGET_FUNC { get => GetOffsetFromIni("c2sSelectTarget", "0x1F0330"); }
        public override byte[] C2S_SELECT_TARGET_SIG => new byte[] { 0x56, 0x6A, 0x06, 0xE8 };
        // c2s_SendCmdNormalAttack(byte pvpMask), команда 0x03, пакет 3 байта. Стоит сразу за выбором цели.
        // Код: push esi; push 1; call 0x6AFC40; push 3; call alloc ... mov word [esi], 3. Персонаж сам подбегает к цели
        public override uint C2S_NORMAL_ATTACK_FUNC { get => GetOffsetFromIni("c2sNormalAttack", "0x1F0370"); }
        public override byte[] C2S_NORMAL_ATTACK_SIG => new byte[] { 0x56, 0x6A, 0x01, 0xE8 };
        // c2s_SendCmdPickup(int id, int tid), команда 0x06, пакет 10 байт. Код: push esi; push 0Ah; call alloc ... mov word [esi], 6.
        // К предмету персонаж НЕ подходит: сервер поднимает только в радиусе ~10 м (проверено: 9.8 м да, 10.2 м нет).
        // Единственный вызывающий в клиенте — 0x5CC050 (ограничитель частоты), его вызывает 0x46E930 (логика клиента)
        public override uint C2S_PICKUP_FUNC { get => 0x1F03B0; }
        public override byte[] C2S_PICKUP_SIG => new byte[] { 0x56, 0x6A, 0x0A, 0xE8 };
        // c2s_SendCmdUnselect(), команда 0x08, пакет 2 байта, без параметров (аналог Esc). Код: push esi; push 2; call alloc ... mov word [esi], 8
        public override uint C2S_UNSELECT_FUNC { get => 0x1F0C90; }
        public override byte[] C2S_UNSELECT_SIG => new byte[] { 0x56, 0x6A, 0x02, 0xE8 };
        // c2s_SendCmdSummonPet(int index), команда 0x64, пакет 6 байт. index = клетка - 1 (ячейка PET_CAGES_ARRAY). Призыв идёт ~3.5 с.
        // Найдено по вызовам: клиент перед вызовом берёт пета из [перс+PET_STRUCT_OFFSET]+0x10+index*4 и проверяет, что он жив.
        // Рядом: 0x65 @0x5F1F80 — отозвать (без параметров), 0x66 @0x5F1FC0 — отпустить пета (НЕ вызывать), 0x63 — не пет.
        // [перс+PET_STRUCT_OFFSET]+0x8 — номер призванной клетки (-1, если пета нет)
        public override uint C2S_SUMMON_PET_FUNC { get => 0x1F1F40; }
        public override byte[] C2S_SUMMON_PET_SIG => new byte[] { 0x56, 0x6A, 0x06, 0xE8 };
        // c2s_SendCmdPetCtrl(int target, int cmd, void* data, int size), команда 0x67, пакет 10 + size байт.
        // Стоит в таблице сразу за командами пета 0x63–0x66 (призвать/отозвать/отпустить). Код: push ebx; push ebp; push esi; mov esi,[esp+1Ch].
        // Номера приказов — по вызовам из клиента (через ограничитель 0x5CC430, вызовы ~0x52DC00–0x52E080):
        //   1 атаковать (data: 1 байт pvpMask, цель — текущая цель персонажа; это Alt+1), 2 следовать/стоять (int),
        //   3 режим агрессии (int 0/1/2), 4 скилл пета (5 байт), 5 ещё один режим (int)
        public override uint C2S_PET_CTRL_FUNC { get => 0x1F2000; }
        public override byte[] C2S_PET_CTRL_SIG => new byte[] { 0x53, 0x55, 0x56, 0x8B, 0x74, 0x24, 0x1C };

        // Предметы на земле: [[game + MOB_OFFSET_1] + GROUND_ITEMS_OFFSET] + MOB_STRUCT_OFFSET, устроены как список мобов.
        // Найдено перебором менеджеров рядом со списком мобов (мир +0x10...+0x3C): здесь объекты с ID вида 0xC0000000|n
        // (у мобов 0x80000000|n) и координатами рядом с персонажем. Название предмета: указатель +0x164 на строку UTF-16
        public override uint GROUND_ITEMS_OFFSET { get => 0x28; }
        public override uint ITEM_ID_OFFSET { get => 0x10C; }   // id для подбора
        public override uint ITEM_TID_OFFSET { get => 0x110; }  // id типа предмета (монета 3044 и т.п.), второй параметр подбора
        public override uint ITEM_KIND_OFFSET { get => 0x14C; } // ItemReader.KIND_*: 1 предмет, 2 ресурс (копать, не подбирать), 3 монеты
        public override uint ITEM_DIST_OFFSET { get => 0x154; } // расстояние до персонажа, float (сверено с координатами)
    }
}
