#!/usr/bin/env python3
"""
Migra una vista Avalonia (.axaml) a los tokens del sistema de diseño (docs/DESIGN_SYSTEM.md).

Sustituye, solo en atributos de pincel, tamaño de letra y radio:
  · colores #RRGGBB conocidos      -> {DynamicResource Brush.*}
  · FontSize fuera de la escala     -> 11 / 12 / 13 / 14 / 16 / 20 / 28
  · CornerRadius numérico           -> {DynamicResource Radius.S|M|L|XL.Top|Pill}

Lo que no puede resolver (colores sin equivalente, caracteres Unicode como iconos, textos fijos)
lo lista al final para tratarlo a mano. No toca nada si se usa --check.

Uso:
    python Scripts/Migrate-UiTokens.py src/VpsDesk.UI/Views/MobileStorageView.axaml
    python Scripts/Migrate-UiTokens.py --check src/VpsDesk.UI/Views/*.axaml
"""
import re
import sys

BRUSH = {
    # superficies
    "121826": "Surface.Card", "121720": "Surface.Card",
    "0E131B": "Surface.Inset", "10141C": "Surface.Inset", "101720": "Surface.Inset",
    "151B25": "Surface.Raised", "10141D": "Surface.Raised", "161E2C": "Surface.Raised",
    "0E1219": "Surface.Bar", "0B0E14": "Surface.Bar",
    "080B10": "Surface.Code", "05080C": "Surface.Code", "070A0F": "Surface.Code",
    # bordes
    "1E2735": "Border.Subtle", "1A2230": "Border.Subtle", "202734": "Border.Subtle", "202A38": "Border.Subtle", "1A2130": "Border.Subtle",
    "2A3444": "Border.Strong", "2A3A52": "Border.Strong", "243042": "Border.Strong", "314158": "Border.Strong",
    # texto
    "E8EDF5": "Text.Primary", "F8FAFC": "Text.Primary", "F1F5F9": "Text.Primary", "C7D0DD": "Text.Primary",
    "9AA4B2": "Text.Secondary", "A8B4C5": "Text.Secondary", "91A0B5": "Text.Secondary", "A9B8CA": "Text.Secondary", "94A3B8": "Text.Secondary",
    "8290A3": "Text.Tertiary", "66758A": "Text.Tertiary", "6B7686": "Text.Tertiary", "56657A": "Text.Tertiary",
    "5B687C": "Text.Tertiary", "465468": "Text.Tertiary", "4A5568": "Text.Tertiary", "64748B": "Text.Tertiary",
    "CBD5E1": "Code.Text",
    # acento y semánticos
    "0F766E": "Accent", "14B8A6": "Accent", "5EEAD4": "Accent.Text", "7EE7CD": "Accent.Text", "8DE7CE": "Accent.Text", "6EE7B7": "Accent.Text",
    "102A24": "Accent.Subtle",
    "34D399": "Success", "0D241F": "Success.Subtle", "0F241C": "Success.Subtle",
    "FBBF24": "Warning", "FCD34D": "Warning", "2A2410": "Warning.Subtle", "201A12": "Warning.Subtle",
    "F87171": "Danger", "FCA5A5": "Danger", "2A1416": "Danger.Subtle", "2A1215": "Danger.Subtle",
    "60A5FA": "Info", "9CC8F5": "Info", "101B2A": "Info.Subtle", "10203A": "Info.Subtle",
}
BRUSH_PROPS = {"Background", "Foreground", "BorderBrush", "Fill", "Stroke", "SelectionBrush", "CaretBrush"}

FONT = {8.5: 11, 9: 11, 9.5: 11, 10: 11, 10.5: 11, 11: 11, 11.5: 12, 12: 12, 12.5: 13, 13: 13, 13.5: 14, 14: 14,
        14.5: 14, 15: 16, 15.5: 16, 16: 16, 17: 16, 18: 20, 19: 20, 20: 20, 21: 20, 22: 20, 24: 28, 25: 28, 27: 28,
        28: 28, 30: 28, 32: 28, 34: 28}

RADIUS = {5: "S", 6: "S", 7: "S", 8: "S", 9: "M", 10: "M", 11: "M", 12: "M", 13: "M", 14: "M",
          15: "L", 16: "L", 17: "L", 18: "L", 20: "L", 21: "XL.Top", 22: "XL.Top", 24: "XL.Top"}


def migrate(text):
    unmapped = set()

    def color(m):
        prop, hex_ = m.group(1), m.group(2).upper()
        if prop not in BRUSH_PROPS and prop != "Value":
            return m.group(0)
        token = BRUSH.get(hex_)
        if token is None:
            unmapped.add("#" + hex_)
            return m.group(0)
        return f'{prop}="{{DynamicResource Brush.{token}}}"'

    # Value="#RRGGBB" solo es un pincel si el Setter es de pincel
    def setter(m):
        prop, hex_ = m.group(1), m.group(2).upper()
        if prop not in BRUSH_PROPS:
            return m.group(0)
        token = BRUSH.get(hex_)
        if token is None:
            unmapped.add("#" + hex_)
            return m.group(0)
        return f'<Setter Property="{prop}" Value="{{DynamicResource Brush.{token}}}"'

    text = re.sub(r'<Setter Property="(\w+)" Value="#([0-9A-Fa-f]{6})"', setter, text)
    text = re.sub(r'\b(Background|Foreground|BorderBrush|Fill|Stroke|SelectionBrush|CaretBrush)="#([0-9A-Fa-f]{6})"', color, text)

    def font(m):
        size = float(m.group(1))
        target = FONT.get(size)
        return m.group(0) if target is None or target == size else f'FontSize="{target}"'

    text = re.sub(r'FontSize="([0-9.]+)"', font, text)
    text = re.sub(r'(<Setter Property="FontSize" Value=")([0-9.]+)(")',
                  lambda m: m.group(1) + str(FONT.get(float(m.group(2)), m.group(2))) + m.group(3), text)

    def radius(m):
        n = int(m.group(1))
        token = RADIUS.get(n)
        return m.group(0) if token is None else f'CornerRadius="{{DynamicResource Radius.{token}}}"'

    text = re.sub(r'CornerRadius="(\d+)"', radius, text)
    text = text.replace('CornerRadius="22,22,0,0"', 'CornerRadius="{DynamicResource Radius.XL.Top}"')
    text = text.replace('CornerRadius="24,24,0,0"', 'CornerRadius="{DynamicResource Radius.XL.Top}"')
    return text, unmapped


def report(path, text, unmapped):
    notes = []
    if unmapped:
        notes.append("colores sin token: " + ", ".join(sorted(unmapped)))
    icons = re.findall(r'&#(?:8[0-9]{3}|9[0-9]{3}|10[0-9]{3});', text)
    icons += re.findall(r'(?:Text|Content)="[^"]*[←-⯿][^"]*"', text)
    if icons:
        notes.append(f"{len(icons)} icono(s) Unicode -> usar <Path Classes=\"icon\" Data=\"{{StaticResource Icon.*}}\">")
    fixed = re.findall(r'(?:Text|Content|PlaceholderText|Header)="[^"{&][^"]{3,}"', text)
    if fixed:
        notes.append(f"{len(fixed)} texto(s) fijo(s) -> {{DynamicResource Clave}} en es-ES y en-US")
    small = re.findall(r'<Button[^>]*(?:Width|Height)="(?:2\d|3\d|4[0-3])"', text)
    if small:
        notes.append(f"{len(small)} botón(es) < 44 px -> Classes=\"btn-icon\" o MinHeight=48")
    print(f"{path}: " + ("; ".join(notes) if notes else "sin pendientes manuales"))


def main(argv):
    check = "--check" in argv
    files = [a for a in argv if not a.startswith("--")]
    if not files:
        print(__doc__)
        return 1
    for path in files:
        original = open(path, encoding="utf-8").read()
        migrated, unmapped = migrate(original)
        if not check and migrated != original:
            open(path, "w", encoding="utf-8", newline="").write(migrated)
        report(path, migrated, unmapped)
    return 0


if __name__ == "__main__":
    sys.exit(main(sys.argv[1:]))
