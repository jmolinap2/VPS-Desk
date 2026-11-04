using System.Text;

namespace VpsDesk.Application.Logging;

/// <summary>Interpreta controles de progreso dentro de la salida de un paso.</summary>
public sealed class TerminalTextBuffer
{
    private readonly List<StringBuilder> _lines = [new()];
    private readonly StringBuilder _escape = new();
    private int _row, _column, _state;

    public void Append(string text)
    {
        foreach (var c in text)
        {
            if (_state == 3) // OSC: título/hipervínculo, hasta BEL o ST.
            {
                if (c == '\a') _state = 0;
                else if (c == '\x1b') _state = 4;
                continue;
            }
            if (_state == 4) { _state = c == '\\' ? 0 : 3; continue; }
            if (_state == 1)
            {
                _state = c == '[' ? 2 : c == ']' ? 3 : 0;
                _escape.Clear();
                continue;
            }
            if (_state == 2)
            {
                if (c is >= '@' and <= '~') { ApplyEscape(c); _state = 0; }
                else if (_escape.Length < 64) _escape.Append(c);
                else _state = 0;
                continue;
            }
            switch (c)
            {
                case '\x1b': _state = 1; break;
                case '\r': _column = 0; break;
                case '\n': _row++; _column = 0; EnsureRow(); break;
                case '\b': _column = Math.Max(0, _column - 1); break;
                case '\t': do { Write(' '); } while (_column % 8 != 0); break;
                default: if (!char.IsControl(c)) Write(c); break;
            }
        }
    }

    public string GetText(bool holdTrailingToken = false)
    {
        var text = string.Join("\n", _lines.Select(x => x.ToString().TrimEnd()));
        // Un token incompleto podría contener un secreto dividido entre dos lecturas.
        // Se publica al llegar su delimitador o al cerrar el flujo, tras sanearlo.
        if (holdTrailingToken && _lines[_row].Length > 0
            && !char.IsWhiteSpace(_lines[_row][^1]) && _row == _lines.Count - 1)
        {
            var end = text.Length;
            while (end > 0 && !char.IsWhiteSpace(text[end - 1])) end--;
            text = text[..end];
        }
        return text;
    }

    private void EnsureRow() { while (_lines.Count <= _row) _lines.Add(new()); }
    private void Write(char c)
    {
        var line = _lines[_row];
        while (line.Length <= _column) line.Append(' ');
        line[_column++] = c;
    }

    private void ApplyEscape(char command)
    {
        var args = _escape.ToString().Split(';');
        int Arg(int i, int fallback = 1) => i < args.Length && int.TryParse(args[i], out var n)
            ? Math.Clamp(n, 0, 10000) : fallback;
        var count = Math.Max(1, Arg(0));
        switch (command)
        {
            case 'A': _row = Math.Max(0, _row - count); break;
            case 'B': _row = Math.Min(_lines.Count - 1, _row + count); break;
            case 'C': _column = Math.Min(10000, _column + count); break;
            case 'D': _column = Math.Max(0, _column - count); break;
            case 'G': _column = Math.Max(0, count - 1); break;
            case 'H': case 'f':
                _row = Math.Clamp(count - 1, 0, _lines.Count - 1);
                _column = Math.Max(0, Arg(1) - 1); break;
            case 'K':
                var line = _lines[_row];
                if (Arg(0, 0) == 2) line.Clear();
                else if (Arg(0, 0) == 1)
                { for (var i = 0; i <= _column && i < line.Length; i++) line[i] = ' '; }
                else if (_column < line.Length) line.Length = _column;
                break;
            // Colores y modos del cursor no se trasladan al cuadro de texto.
        }
    }
}
