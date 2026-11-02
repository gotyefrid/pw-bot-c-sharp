#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""Сделать диск виртуальной машины VMware «обычным» для игры.

Зачем: сервер Perfect World (Comeback 1.4.6) не пускает в игру, если клиент
присылает модель и серийник виртуального диска VMware. Клиент читает
дескриптор через \\\\.\\PhysicalDrive0 и отправляет модель и серийник на сервер.

Что подменяем:
  * модель   — ключи scsi0:0.vendorID / scsi0:0.productID в .vmx
  * серийник — ddb.uuid в дескрипторе .vmdk (первые 4 байта задают начало строки)

Проверено 2026-11-02 на VMware Workstation 26.0.1, гость Windows 11 x64:
  Get-PhysicalDisk -> Model "ST1000DM010-2EP1",
                      Serial "5000c500b73d9241c8551a6f04d2889e"
С этим набором игра зашла в виртуалке на НЕПАТЧЕННОМ клиенте.

Запуск (VM выключена):
    python -I vm-storage.py "C:\\Users\\ВЫ\\Documents\\Virtual Machines\\Новая VM\\Новая VM.vmx"

Ключи:
    --model ИМЯ      модель диска (по умолчанию ST1000DM010-2EP1)
    --keep-uuid      не менять ddb.uuid, только модель
    --dry-run        показать, что будет сделано, ничего не записывать

Скрипт читает только первые 64 КБ файла .vmdk (там дескриптор), а не весь
диск — он может быть в десятки гигабайт.
"""
import argparse
import os
import random
import re
import shutil
import subprocess
import sys

SEARCH_SIZE = 65536          # сколько читать из начала .vmdk

# Модель -> 4 первых байта ddb.uuid, которые дают нужное начало серийника.
# Формат строки: "50 00 <байт> <байт>". Проверено опытом на ST1000DM010-2EP1:
# ddb.uuid "50 00 C5 00 ..." -> серийник 5000c500...
# Для дисков других марок эти байты надо определить так же — опытом.
MODELS = {
    "ST1000DM010-2EP1": "50 00 C5 00",   # Seagate Barracuda 1 ТБ, проверено
    "ST2000DM008-2FR1": "50 00 C5 00",   # Seagate Barracuda 2 ТБ, тот же OUI
}
DEFAULT_MODEL = "ST1000DM010-2EP1"

# VMware всегда пишет ddb.uuid, начинающийся с этих байт (60 00 C2 — её OUI).
# Встречались 60 00 C2 99 и 60 00 C2 98; на всякий случай смотрим первые три байта.
VMWARE_UUID_PREFIX = "60 00 C2"


def die(msg):
    print("ОШИБКА: " + msg)
    sys.exit(1)


def ensure_vm_off(vmx_path):
    """Остановиться, если запущена ИМЕННО эта VM (её .lck) или есть её vmware-vmx."""
    d = os.path.dirname(vmx_path)
    name = os.path.splitext(os.path.basename(vmx_path))[0].lower()
    lcks = [n for n in os.listdir(d) if n.endswith(".lck")]
    if lcks:
        die("VM запущена (рядом лежит %s). Выключи её и закрой VMware Workstation."
            % ", ".join(lcks))
    try:
        out = subprocess.run(["wmic", "process", "where",
                              "name='vmware-vmx.exe'", "get", "CommandLine"],
                             capture_output=True, text=True, errors="replace").stdout
    except OSError:
        out = ""
    for line in out.splitlines():
        if name in line.lower():
            die("запущен vmware-vmx.exe для этой VM — сначала выключи её.")
    # если wmic недоступен — грубая, но безопасная проверка
    if not out.strip():
        try:
            t = subprocess.run(["tasklist"], capture_output=True, text=True,
                               errors="replace").stdout.lower()
            if "vmware-vmx.exe" in t:
                print("  ВНИМАНИЕ: запущен vmware-vmx.exe (возможно, другая VM). "
                      "Если это эта VM — выключи её.")
        except OSError:
            pass


def read_vmx(path):
    with open(path, "rb") as f:
        return f.read()


def split_vmx(raw):
    """Строки .vmx без учёта того, CRLF там или LF."""
    if b"\r\n" in raw:
        return raw.split(b"\r\n"), b"\r\n"
    return raw.split(b"\n"), b"\n"


def find_vmdk(vmx_raw, vmx_path):
    """Файл диска из .vmx (scsiN:M.fileName / nvmeN:M.fileName / sataN:M.fileName)."""
    txt = vmx_raw.decode("latin1")
    for line in txt.splitlines():
        m = re.match(r'\s*(?:scsi|nvme|sata)\d+:\d+\.fileName\s*=\s*"([^"]+\.vmdk)"',
                     line, re.I)
        if not m:
            continue
        cand = m.group(1)
        if "$" in cand or "[" in cand:
            continue
        full = os.path.join(os.path.dirname(vmx_path), cand)
        if os.path.exists(full):
            return full
    return None


def patch_vmdk_uuid(vmdk_path, model, dry_run):
    with open(vmdk_path, "rb") as f:
        head = f.read(SEARCH_SIZE)
    text = head.decode("latin1")

    m = re.search(r'ddb\.uuid = "([^"]*)"', text)
    if not m:
        die('в дескрипторе %s нет строки ddb.uuid — этот формат мы не проверяли'
            % os.path.basename(vmdk_path))
    old = m.group(1)
    if not old.startswith(VMWARE_UUID_PREFIX):
        print("  ddb.uuid уже не VMware-шный: %s — пропускаю" % old)
        return None

    prefix = MODELS[model]
    body = ["%02X" % random.randrange(256) for _ in range(12)]
    parts = prefix.split() + body                     # 4 + 12 = 16 байт
    new = " ".join(parts[0:8]) + "-" + " ".join(parts[8:16])

    print("  ddb.uuid: %s" % old)
    print("         -> %s" % new)
    print("  серийник будет: %s" % "".join(parts).lower())

    if dry_run:
        return new
    if len(new) != len(old):
        die("новая строка ddb.uuid другой длины (%d != %d) — прервано"
            % (len(new), len(old)))

    with open(vmdk_path, "r+b") as f:
        f.seek(text.index(old))
        f.write(new.encode("ascii"))
    return new


def patch_vmx(vmx_path, vmdk_basename, model, dry_run):
    raw = read_vmx(vmx_path)
    lines, eol = split_vmx(raw)
    txt = eol.decode("latin1").join(ln.decode("latin1") for ln in lines)
    changed = []

    # 1. Убрать nvme — на нём модель и серийник не подменить.
    kept = [ln for ln in lines if not re.match(rb"\s*nvme\d", ln, re.I)]
    if len(kept) != len(lines):
        changed.append("убрано строк nvme: %d" % (len(lines) - len(kept)))
        txt = eol.decode("latin1").join(ln.decode("latin1") for ln in kept)

    # 2. Если диск не на SCSI — перевесить его на scsi0:0.
    if not re.search(r"^\s*scsi0:0\.fileName", txt, re.M):
        st = eol.decode("latin1")
        m = re.search(r'^\s*(?:sata|nvme)\d+:\d+\.fileName\s*=\s*"([^"]+\.vmdk)"',
                      txt, re.M | re.I)
        if m:
            txt = txt.replace(m.group(0), 'scsi0:0.fileName = "%s"' % m.group(1))
            changed.append("диск перевешен на scsi0:0")
        else:
            txt = txt.rstrip("\r\n" + st) + st + 'scsi0:0.fileName = "%s"' % vmdk_basename
            changed.append("добавлен scsi0:0.fileName")

    # 3. Обязательные ключи контроллера.
    add = []
    for key, val in (("scsi0.present", "TRUE"),
                     ("scsi0.virtualDev", "lsisas1068"),
                     ("scsi0:0.present", "TRUE"),
                     ("disk.EnableUUID", "TRUE")):
        pat = re.compile(r"^(\s*)%s\s*=\s*\"[^\"]*\"" % re.escape(key), re.M)
        if pat.search(txt):
            t2 = pat.sub(lambda mm: '%s%s = "%s"' % (mm.group(1), key, val), txt)
            if t2 != txt:
                changed.append("%s = %s" % (key, val))
            txt = t2
        else:
            add.append('%s = "%s"' % (key, val))

    # 4. Модель.
    for key, val in (("scsi0:0.vendorID", "ATA"),
                     ("scsi0:0.productID", model)):
        pat = re.compile(r"^(\s*)%s\s*=\s*\"[^\"]*\"" % re.escape(key), re.M)
        if pat.search(txt):
            t2 = pat.sub(lambda mm: '%s%s = "%s"' % (mm.group(1), key, val), txt)
            if t2 != txt:
                changed.append("%s = %s" % (key, val))
            txt = t2
        else:
            add.append('%s = "%s"' % (key, val))

    if add:
        st = eol.decode("latin1")
        txt = txt.rstrip("\r\n" + st) + st + st.join(add) + st
        changed.append("добавлено: " + ", ".join(a.split(" = ")[0] for a in add))

    for c in changed:
        print("  " + c)
    if dry_run or not changed:
        return

    bak = vmx_path + ".bak-storage"
    if not os.path.exists(bak):
        shutil.copy2(vmx_path, bak)
        print("  бэкап .vmx: " + os.path.basename(bak))
    with open(vmx_path, "wb") as f:
        f.write(txt.encode("latin1"))


def main():
    ap = argparse.ArgumentParser(description="Сделать диск VMware «обычным» для игры")
    ap.add_argument("vmx", help="путь к .vmx виртуальной машины")
    ap.add_argument("--model", default=DEFAULT_MODEL,
                    help="модель диска (по умолчанию %(default)s)")
    ap.add_argument("--keep-uuid", action="store_true", help="не менять ddb.uuid")
    ap.add_argument("--dry-run", action="store_true", help="только показать, ничего не писать")
    a = ap.parse_args()

    vmx = os.path.abspath(a.vmx)
    if not os.path.exists(vmx):
        die("нет файла " + vmx)
    if a.model not in MODELS:
        die("модели %r нет в таблице MODELS — добавь её в скрипт вместе с началом "
            "ddb.uuid (иначе модель и серийник будут от разных марок)." % a.model)

    print("VM: " + vmx)
    ensure_vm_off(vmx)
    print("VM выключена — ок")

    vmdk = find_vmdk(read_vmx(vmx), vmx)
    if vmdk is None:
        die("в .vmx не нашёл .vmdk — проверь, что диск добавлен в VMware")
    print("диск: %s" % vmdk)

    if not a.keep_uuid:
        print("\n-- серийник (ddb.uuid) --")
        patch_vmdk_uuid(vmdk, a.model, a.dry_run)
    print("\n-- модель (.vmx) --")
    patch_vmx(vmx, os.path.basename(vmdk), a.model, a.dry_run)

    if a.dry_run:
        print("\n(dry-run: ничего не записано)")
        return
    print("\nГотово. Запусти VM и проверь в госте:")
    print("  Get-PhysicalDisk | Format-Table DeviceId, FriendlyName, Model, "
          "SerialNumber, BusType -AutoSize")
    print("Ожидается: Model = %s, Serial начинается с %s"
          % (a.model, "".join(MODELS[a.model].split()).lower()))


if __name__ == "__main__":
    main()
