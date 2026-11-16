"""Скиллы персонажа в живом клиенте PW Classic 1.3.6: id, уровень, ярость, мана, время атаки,
перезарядка, подготовка, оружие, статус. Только чтение памяти; адреса персонажа берутся у Notes.Probe.

python tools\\pwskill.py [--pid 1234] [--text] [--raw]
"""
import ctypes
import ctypes.wintypes as wt
import re
import struct
import subprocess
import sys
from pathlib import Path

# pwclassic136: профиль src/Notes.Core/Profiles/pwclassic136.jsonc
HOST = {"skills": 0x1504, "skillsCount": 0x1508}
SKILL = {"id": 0x8, "level": 0xC, "cooldown": 0x14}
CHI = 0x34          # описание (stub): стоимость в ярости
ELEMENT = 0x4       # объект скилла -> ElementSkill
STUB = 0x4          # ElementSkill -> описание (stub)
RANGE = 0x8         # ElementSkill: дальность float (0 — ближний бой)
MANA_SLOT = 0x20    # таблица описания: функция стоимости маны
ATTACK_SLOT = 0x24  # таблица описания: время атаки, мс

PROCESS_VM_READ = 0x0010
PROCESS_QUERY_INFORMATION = 0x0400
TH32CS_SNAPPROCESS = 0x00000002


class PROCESSENTRY32W(ctypes.Structure):
    _fields_ = [
        ("dwSize", wt.DWORD), ("cntUsage", wt.DWORD), ("th32ProcessID", wt.DWORD),
        ("th32DefaultHeapID", ctypes.POINTER(ctypes.c_ulong)), ("th32ModuleID", wt.DWORD),
        ("cntThreads", wt.DWORD), ("th32ParentProcessID", wt.DWORD), ("pcPriClassBase", ctypes.c_long),
        ("dwFlags", wt.DWORD), ("szExeFile", ctypes.c_wchar * 260),
    ]


class Client:
    def __init__(self, pid):
        self.kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        self.handle = self.kernel32.OpenProcess(PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, False, pid)
        if not self.handle:
            raise OSError(f"OpenProcess({pid}): {ctypes.get_last_error()}")

    def close(self):
        self.kernel32.CloseHandle(self.handle)

    def read(self, address, size):
        buf = ctypes.create_string_buffer(size)
        got = ctypes.c_size_t()
        if not self.kernel32.ReadProcessMemory(self.handle, ctypes.c_void_p(address), buf, size, ctypes.byref(got)):
            raise OSError(f"ReadProcessMemory(0x{address:X}, {size}): {ctypes.get_last_error()}")
        if got.value != size:
            raise OSError(f"ReadProcessMemory(0x{address:X}): прочитано {got.value} из {size}")
        return buf.raw

    def u32(self, address):
        return struct.unpack("<I", self.read(address, 4))[0]

    def i32(self, address):
        return struct.unpack("<i", self.read(address, 4))[0]

    def f32(self, address):
        return struct.unpack("<f", self.read(address, 4))[0]

    def f64(self, address):
        return struct.unpack("<d", self.read(address, 8))[0]


def find_clients():
    kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
    kernel32.CreateToolhelp32Snapshot.restype = wt.HANDLE
    snap = kernel32.CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0)
    if snap == wt.HANDLE(-1).value:
        raise OSError(f"CreateToolhelp32Snapshot: {ctypes.get_last_error()}")
    entry = PROCESSENTRY32W()
    entry.dwSize = ctypes.sizeof(entry)
    found = []
    try:
        if kernel32.Process32FirstW(snap, ctypes.byref(entry)):
            while True:
                if entry.szExeFile.lower() == "client.exe":
                    found.append(entry.th32ProcessID)
                if not kernel32.Process32NextW(snap, ctypes.byref(entry)):
                    break
    finally:
        kernel32.CloseHandle(snap)
    return found


def probe_path():
    return Path(__file__).resolve().parent / "Notes.Probe" / "bin" / "Release" / "net48" / "Notes.Probe.exe"


def probe(pid, *args):
    exe = probe_path()
    if not exe.exists():
        raise FileNotFoundError(f"Нет {exe} — соберите Release")
    result = subprocess.run([str(exe), *args, str(pid)], capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip())
    return result.stdout


def attach(pid):
    text = probe(pid, "attach")
    game = re.search(r"game\s*=\s*(0x[0-9A-Fa-f]+)", text)
    host = re.search(r"персонаж\s*=\s*(0x[0-9A-Fa-f]+)", text)
    nick = re.search(r"ник\s*=\s*(.+)", text)
    if not game or not host:
        raise RuntimeError("Не разобрал ответ attach:\n" + text)
    return int(game.group(1), 16), int(host.group(1), 16), (nick.group(1).strip() if nick else "?")


def skill_texts(pid, ids):
    text = probe(pid, "skilltext", *[str(i) for i in ids])
    sections = {}
    current = None
    lines = []
    for line in text.splitlines():
        m = re.match(r"^=== (\d+) ===$", line)
        if m:
            if current is not None:
                sections[current] = lines
            current = int(m.group(1))
            lines = []
        elif current is not None:
            lines.append(line)
    if current is not None:
        sections[current] = lines
    return sections


def strip_colors(line):
    return re.sub(r"\^[0-9a-fA-F]{6}", "", line)


def parse_text(lines):
    clean = [strip_colors(x).strip() for x in lines]
    name = next((x for x in clean if x), "")
    data = {"name": name, "cast": "", "attack": "", "cooldown": "", "weapon": "", "status": "", "chiText": "",
            "hasMana": False, "hasCast": False, "hasAttack": False, "hasCooldown": False}
    for line in clean:
        if "Маг. энергия:" in line:
            data["hasMana"] = True
        if "Подготовка:" in line:
            data["hasCast"] = True
            if "Мгновенная" in line:
                data["cast"] = "мгнов"
        if "Время атаки:" in line:
            data["hasAttack"] = True
        if "Перезарядка:" in line:
            data["hasCooldown"] = True
        if m := re.search(r"Подготовка:\s*([0-9.,]+)", line):
            data["cast"] = m.group(1)
        if m := re.search(r"Время атаки:\s*([0-9.,]+)", line):
            data["attack"] = m.group(1)
        if m := re.search(r"Перезарядка:\s*([0-9.,]+)", line):
            data["cooldown"] = m.group(1)
        if m := re.search(r"Ограничения оружия:\s*(.+)", line):
            data["weapon"] = m.group(1).strip()
        if m := re.search(r"Статус:\s*(.+)", line):
            data["status"] = m.group(1).strip()
        if m := re.search(r"требуется\s+(\d+)\s+единиц", line):
            data["chiText"] = m.group(1)
    return data


def read_code(client, address, size=64):
    try:
        return client.read(address, size)
    except OSError:
        return b""


def eval_linear_int(code, level):
    """Линейная формула A*level+B из простых целочисленных инструкций; None — не разобрал."""
    eax = None
    ecx = None
    at = 6
    def imm32(pos):
        return struct.unpack_from("<i", code, pos)[0]
    while at < len(code):
        if code[at:at + 3] == b"\x8B\x40\x28":
            eax = (1, 0); at += 3; continue
        if code[at:at + 3] == b"\x8B\x48\x28":
            ecx = (1, 0); at += 3; continue
        if code[at:at + 3] == b"\x6B\x40\x28":
            eax = (code[at + 3], 0); at += 4; continue
        if code[at:at + 3] == b"\x6B\x48\x28":
            ecx = (code[at + 3], 0); at += 4; continue
        if code[at:at + 2] == b"\x8B\xC8":
            ecx = eax; at += 2; continue
        if code[at:at + 2] == b"\x8B\xC1":
            eax = ecx; at += 2; continue
        if code[at] == 0x83 and code[at + 1] in (0xC0, 0xC1, 0xE8, 0xE9):
            imm = code[at + 2]
            if code[at + 1] == 0xC0: eax = (eax[0], eax[1] + imm)
            elif code[at + 1] == 0xE8: eax = (eax[0], eax[1] - imm)
            elif code[at + 1] == 0xC1: ecx = (ecx[0], ecx[1] + imm)
            else: ecx = (ecx[0], ecx[1] - imm)
            at += 3; continue
        if code[at] == 0x05:
            eax = (eax[0], eax[1] + imm32(at + 1)); at += 5; continue
        if code[at] == 0x2D:
            eax = (eax[0], eax[1] - imm32(at + 1)); at += 5; continue
        if code[at:at + 2] == b"\x2B\xC8":
            ecx = (ecx[0] - eax[0], ecx[1] - eax[1]); at += 2; continue
        if code[at:at + 2] == b"\x2B\xC1":
            eax = (eax[0] - ecx[0], eax[1] - ecx[1]); at += 2; continue
        if code[at:at + 2] == b"\xC1\xE1":
            ecx = (ecx[0] << code[at + 2], ecx[1] << code[at + 2]); at += 3; continue
        if code[at:at + 2] == b"\xC1\xE0":
            eax = (eax[0] << code[at + 2], eax[1] << code[at + 2]); at += 3; continue
        if code[at:at + 3] == b"\x8D\x04\x80":
            eax = (eax[0] * 5, eax[1] * 5); at += 3; continue
        if code[at:at + 3] == b"\x8D\x04\x40":
            eax = (eax[0] * 3, eax[1] * 3); at += 3; continue
        if code[at:at + 3] == b"\x8D\x04\x49":
            eax = (ecx[0] * 3, ecx[1] * 3); at += 3; continue
        if code[at:at + 3] == b"\x8D\x04\x4D":
            eax = (ecx[0] * 2, ecx[1] * 2 + imm32(at + 3)); at += 7; continue
        if code[at:at + 3] == b"\x8D\x0C\xC5":
            ecx = (eax[0] * 8, eax[1] * 8 + imm32(at + 3)); at += 7; continue
        if code[at:at + 3] == b"\x69\xC0":
            imm = imm32(at + 3); eax = (eax[0] * imm, eax[1] * imm); at += 7; continue
        if code[at:at + 3] == b"\x69\xC1":
            imm = imm32(at + 3); eax = (ecx[0] * imm, ecx[1] * imm); at += 7; continue
        if code[at:at + 3] == b"\x89\x45\x08":
            return eax[0] * level + eax[1] if eax else None
        if code[at:at + 3] == b"\x89\x4D\x08":
            return ecx[0] * level + ecx[1] if ecx else None
        if code[at:at + 3] == b"\xDB\x45\x08":
            return eax[0] * level + eax[1] if eax else None
        if code[at:at + 3] == b"\xDB\x4D\x08":
            return ecx[0] * level + ecx[1] if ecx else None
        return None
    return None


def eval_property(client, function, element, level):
    """Разбирает маленькую функцию из таблицы описания (int/float, уровень, таблица)."""
    code = read_code(client, function, 96)
    if len(code) < 8:
        return None
    if code[0] == 0xB8 and code[5:8] == b"\xC2\x04\x00":
        return ("int", struct.unpack_from("<i", code, 1)[0])
    if code[0:2] == b"\xD9\x05" and code[6:9] == b"\xC2\x04\x00":
        return ("float", client.f32(struct.unpack_from("<I", code, 2)[0]))
    if code[0:2] == b"\x33\xC0":
        return ("int", 0)
    if code[0] == 0xB0 and code[1] in (0, 1) and code[2:5] == b"\xC2\x04\x00":
        return ("int", code[1])
    if code[0:6] == b"\x55\x8B\xEC\x8B\x45\x08":
        if code[6] == 0xD9 and code[7] == 0x40:
            return ("float", client.f32(element + code[8]))
        if code[6:12] == b"\x8B\x40\x28\x8B\x04\x85":
            table = struct.unpack_from("<I", code, 12)[0]
            return ("int", client.i32(table + level * 4))
        if code[6:16] == b"\x8B\x40\x28\x8D\x0C\xC5\x00\x00\x00\x00" and code[16:18] == b"\x2B\xC8":
            value = 7 * level
            at = 18
            if code[at:at + 2] == b"\x83\xE9":
                value -= code[at + 2]
            elif code[at:at + 2] == b"\x83\xC1":
                value += code[at + 2]
            elif code[at] == 0x2D:
                value -= struct.unpack_from("<i", code, at + 1)[0]
            elif code[at] == 0x05:
                value += struct.unpack_from("<i", code, at + 1)[0]
            elif code[at:at + 2] == b"\x66\x0F":
                if code[at + 2:at + 4] == b"\x6E\xC1" and code[at + 4:at + 8] == b"\xF3\x0F\xE6\xC0":
                    op = code[at + 8:at + 11]
                    if op in (b"\xF2\x0F\x58", b"\xF2\x0F\x5C"):
                        constant = client.f64(struct.unpack_from("<I", code, at + 12)[0])
                        value = value + constant if op == b"\xF2\x0F\x58" else value - constant
                    else:
                        return None
                else:
                    return None
            else:
                return None
            return ("float", float(value))
        if code[6:11] == b"\x66\x0F\x6E\x40\x28" and code[11:15] == b"\xF3\x0F\xE6\xC0":
            at = 15
            if code[at:at + 3] != b"\xF2\x0F\x59":
                return None
            mul = client.f64(struct.unpack_from("<I", code, at + 4)[0])
            at += 8
            if code[at:at + 3] not in (b"\xF2\x0F\x5C", b"\xF2\x0F\x58"):
                return None
            add = client.f64(struct.unpack_from("<I", code, at + 4)[0])
            value = level * mul
            value = value - add if code[at:at + 3] == b"\xF2\x0F\x5C" else value + add
            return ("float", value)
    linear = eval_linear_int(code, level)
    if linear is not None:
        return ("int", linear)
    return None


def fmt(value):
    if value is None:
        return "?"
    kind, number = value
    if kind == "int":
        return str(number)
    return f"{number:.1f}"


def main(argv):
    pid = None
    show_text = "--text" in argv
    show_raw = "--raw" in argv
    if "--pid" in argv:
        pid = int(argv[argv.index("--pid") + 1])
    if pid is None:
        clients = find_clients()
        if len(clients) != 1:
            print("Клиентов client.exe: " + (", ".join(map(str, clients)) or "нет") + ". Укажите --pid", file=sys.stderr)
            return 2
        pid = clients[0]

    _, host, nick = attach(pid)
    client = Client(pid)
    try:
        array = client.u32(host + HOST["skills"])
        count = client.i32(host + HOST["skillsCount"])
        skills = []
        for i in range(min(count, 256)):
            skill = client.u32(array + i * 4)
            if not skill:
                continue
            element = client.u32(skill + ELEMENT)
            stub = client.u32(element + STUB) if element else 0
            table = client.u32(stub) if stub else 0
            level = client.i32(skill + SKILL["level"])
            skills.append({
                "id": client.i32(skill + SKILL["id"]),
                "level": level,
                "cooldown": client.i32(skill + SKILL["cooldown"]),
                "chi": client.i32(stub + CHI) if stub else 0,
                "range": client.f32(element + RANGE) if element else 0.0,
                "mana": eval_property(client, client.u32(table + MANA_SLOT), element, level) if table else None,
                "attack": eval_property(client, client.u32(table + ATTACK_SLOT), element, level) if table else None,
            })

        texts = skill_texts(pid, [s["id"] for s in skills])
        print(f"Клиент PID {pid}, персонаж {nick}, скиллов {len(skills)}")
        print(f"{'ID':>4} {'Имя':<28} {'Ур':>2} {'Яр':>4} {'Мана':>6} {'Атака':>6} {'КД':>5} {'Подг':>5} {'Дальн':>5} {'Оружие':<20} Статус")
        for s in skills:
            text = parse_text(texts.get(s["id"], []))
            attack = s["attack"][1] if s["attack"] else (text["attack"] or ("—" if not text["hasAttack"] else "?"))
            cast = text["cast"] or "—"
            mana = fmt(s["mana"]) if s["mana"] else ("—" if not text["hasMana"] else "?")
            rng = f"{s['range']:.1f}" if s["range"] else "ближ"
            weapon = text["weapon"] or "—"
            status = text["status"] or "—"
            print(f"{s['id']:>4} {text['name'][:28]:<28} {s['level']:>2} {s['chi']:>4} "
                  f"{mana:>6} {str(attack):>6} {s['cooldown']:>5} {cast:>5} {rng:>5} "
                  f"{weapon[:20]:<20} {status}")
            if show_raw:
                print(f"     chi={s['chi']} range={s['range']} cooldown={s['cooldown']} "
                      f"mana={s['mana']} attack={s['attack']}")
            if show_text:
                for line in texts.get(s["id"], []):
                    print("     " + strip_colors(line))
        return 0
    finally:
        client.close()


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
