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
        // Идти в точку — то, что делает клик по земле (проверено в игре: дошёл до точки, остановился в 0.3 м):
        //   work = WorkMan->CreateWork(1)          0x466C70 (thiscall; тип 1 = CECHPWorkMove)
        //   work->SetDestination(0, &point)        0x46A890 (thiscall; 0 — точка на земле, 1 — 3D (полёт?), 2 — НАПРАВЛЕНИЕ:
        //                                                    бежит по вектору без остановки, 3 — толчок)
        //   WorkMan->StartWork(1, work, 1, 0)      0x467070 (thiscall)
        // WorkMan = [перс+0xE48] (CECHPWorkMan). Точка — 3 float: X, высота, Y (как в объекте с +0x3C).
        public override uint HOST_WORKMAN_OFFSET { get => 0xE48; }
        public override uint WORK_CREATE_FUNC { get => 0x66C70; }
        public override byte[] WORK_CREATE_SIG => new byte[] { 0x64, 0xA1, 0x00, 0x00, 0x00, 0x00 };
        public override uint WORK_MOVE_SET_DEST_FUNC { get => 0x6A890; }
        public override byte[] WORK_MOVE_SET_DEST_SIG => new byte[] { 0x8B, 0x44, 0x24, 0x08, 0x83, 0xEC };
        public override uint WORK_START_FUNC { get => 0x67070; }
        public override byte[] WORK_START_SIG => new byte[] { 0x8A, 0x44, 0x24, 0x10, 0x53, 0x8B };
        public override uint MOB_LOC_OFFSET { get => 0x3C; }
        // CECHostPlayer::ApplySkill(int id, bool, int target, int pvp) — то, что делает нажатие кнопки скилла (thiscall, this = перс).
        // Из интерфейса клиент вызывает (id, 0, 0, -1): цель 0 = текущая цель персонажа, -1 = обычный режим PvP.
        // Если цель дальше дальности скилла — создаёт «подойти к цели» (CECHPWorkTrace, тип 2) с причиной 3 = скилл,
        // дойдя, кастует сам. Проверено в игре: моб в 32 м, подошёл до 18.6 м, применил «Жалящий рой».
        // Лечение пета: (330, 0, WID пета, -1) — проверено, скилл применился. Воскрешению пета дальность не нужна — прямая команда.
        // Внутри есть особая проверка на ID 167 (Городской портал). Найдено как вызывающий SetTraceTarget(id, 3).
        public override uint HOST_APPLY_SKILL_FUNC { get => 0x5CC50; }
        public override byte[] HOST_APPLY_SKILL_SIG => new byte[] { 0x53, 0x55, 0x56, 0x57, 0x8B, 0xF1 };
        // CECHostPlayer::PickupObject(int id, bool gather) — то, что делает клик по предмету на земле (thiscall, this = перс).
        // Проверяет, что id — предмет (0xC...), что его вид (+0x14C) совпадает с gather (2 = ресурс), создаёт работу
        // «подойти к цели» (CECHPWorkTrace, тип 2) с причиной 1 = подобрать (4 = собрать ресурс) и запускает её.
        // Дойдя (~3 м), клиент сам отправляет c2s_SendCmdPickup. Проверено в игре: подошёл ~6 м и поднял.
        // Как нашли: по RTTI-именам классов (.?AVCECHPWorkTrace@@) -> vtable -> конструктор -> фабрика работ
        // CECHPWorkMan::CreateWork @0x466C70 (тип 1 идти в точку, 2 подойти к цели, 11 анимация подбора) -> кто создаёт тип 2.
        // [перс+0xE48] — CECHPWorkMan, сам перс — CECHostPlayer (vtable 0x8D74C8).
        public override uint HOST_PICKUP_OBJECT_FUNC { get => 0x62800; }
        public override byte[] HOST_PICKUP_OBJECT_SIG => new byte[] { 0x53, 0x55, 0x57, 0x8B, 0xF9, 0x8B };
        // c2s_SendCmdCastSkill(int skill, byte pvpMask, int count, int* targets), команда 0x29, пакет 8 + 4*count байт:
        // [0x29][skill:4][pvpMask][count][targets...]. Цель зависит от скилла (проверено тестом):
        //   лечение пета — целью сам пет (без цели сервер не принимает), воскрешение пета — без цели (count 0).
        // Признак, что скилл принят — началась перезарядка (SKILL_COOLDOWN_OFFSET > 0); у долгих скиллов только после каста.
        public override uint C2S_CAST_SKILL_FUNC { get => 0x1F0D70; }
        public override byte[] C2S_CAST_SKILL_SIG => new byte[] { 0x53, 0x8B, 0x5C, 0x24 };
        // c2s_SendCmdUseItem(byte where, byte index, int tid, byte count), команда 0x28, пакет 10 байт:
        // [0x28][where][count][index:2][tid:4]. where 0 = сумка, count 1. Работает и для банок, и для корма пета (пет должен быть призван).
        public override uint C2S_USE_ITEM_FUNC { get => 0x1F03F0; }
        public override byte[] C2S_USE_ITEM_SIG => new byte[] { 0x56, 0x6A, 0x0A, 0xE8 };
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

        // Уровень персонажа. Лежит рядом с WID (0x458) и HP (0x46C); 0x474 — похоже, текущий опыт
        public override uint PERS_LEVEL_OFFSET { get => 0x464; }
        // Сумка: [перс+0xC34] — объект сумки, +0xC массив указателей на предметы (индекс = ячейка, с 0), +0x10 число ячеек (32).
        // Найдено так: в сумке лежали подобранные известь (tid 8084) и мех (8083) — искали указатели из структуры перса,
        // ведущие к массиву предметов с этими tid. Совпадение одно.
        public override uint INVENTORY_OFFSET { get => 0xC34; }
        public override uint INV_ITEMS_OFFSET { get => 0xC; }
        public override uint INV_SIZE_OFFSET { get => 0x10; }
        // Предмет в сумке: +0x4 категория (9 банки, 27 корм пета, 8 материалы, 0/15/19 экипировка...), +0x8 tid,
        // +0x10 сколько в стопке, +0x14 максимум в стопке, +0x40 указатель на текст подсказки (название в начале), +0x54 описание (шаблон)
        public override uint INV_ITEM_CATEGORY_OFFSET { get => 0x4; }
        public override uint INV_ITEM_TID_OFFSET { get => 0x8; }
        public override uint INV_ITEM_COUNT_OFFSET { get => 0x10; }
        public override uint INV_ITEM_ESSENCE_OFFSET { get => 0x54; }
        // Описание банки (найдено сравнением малой и средней банок HP и банки MP): +0x0 tid, +0x14C требуемый уровень,
        // +0x154 сколько HP, +0x158 за сколько секунд, +0x15C сколько MP, +0x160 за сколько секунд, +0x164/+0x168 цены
        public override uint ESSENCE_LEVEL_OFFSET { get => 0x14C; }
        public override uint ESSENCE_POTION_HP_OFFSET { get => 0x154; }
        public override uint ESSENCE_POTION_MP_OFFSET { get => 0x15C; }
        // У корма пета другой класс предмета, и описание лежит по другому указателю: +0x4C (у банок +0x54).
        // Описание корма (сравнение трёх кормов на 10/50/100 верности): +0x0 tid, +0x144 класс корма (1/2/3),
        // +0x148 сколько верности, +0x150 вид корма битовой маской (16 = «чистая вода»), +0x154 цена, +0x15C максимум в стопке.
        // Что ест конкретный пет, не ищем: бот проверяет, уменьшилась ли стопка после кормления.
        public override uint INV_FOOD_ESSENCE_OFFSET { get => 0x4C; }
        public override uint ESSENCE_FOOD_LOYALTY_OFFSET { get => 0x148; }

        // Скиллы: [перс+0xE70] — массив указателей на объекты скиллов, [перс+0xE74] — их число. Найдено поиском в структуре перса
        // массива объектов одного класса (общий vtable 0x8DB00C) с небольшими числами внутри.
        // Объект скилла: +0x8 ID, +0xC уровень скилла, +0x10 сколько мс перезарядки осталось (0 — готов),
        // +0x14 полная перезарядка, +0x18 младший бит — флаг «на перезарядке».
        // В объекте скилла названия нет — бот берёт его по ID из файла игры (SkillNames, configs.pck → skillstr.txt).
        // Какой ID за что отвечает, узнали так: применяли скилл руками и смотрели, у кого пошла перезарядка.
        public override uint SKILLS_OFFSET { get => 0xE70; }
        public override uint SKILLS_COUNT_OFFSET { get => 0xE74; }
        public override uint SKILL_ID_OFFSET { get => 0x8; }
        public override uint SKILL_COOLDOWN_OFFSET { get => 0x10; }
        // ID скиллов друида (захардкожено: персонаж с петом пока один). Ещё: 299 «Жалящий рой», 167 «Городской портал»
        public override uint SKILL_HEAL_PET { get => 330; }    // «Исцеление питомца», каст ~1.6 с
        public override uint SKILL_REVIVE_PET { get => 329; }  // «Оживление питомца», каст ~12 с
        // «Жалящий рой» — базовый атакующий скилл друида, есть у всех друидов
        public override uint SKILL_DEFAULT_ATTACK { get => 299; }
        // Точно не атакующие — не показываются в списке «Attack skill». Дописывать сюда по мере нахождения
        public override uint[] SKILLS_NOT_ATTACK => new uint[] { 167, 329, 330 };
    }
}
