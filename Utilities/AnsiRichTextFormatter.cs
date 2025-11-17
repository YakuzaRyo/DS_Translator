using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Windows.UI;

namespace Configer.Utilities
{
    /// <summary>
    /// Converts ANSI escape sequences (SGR) into styled text segments suitable for RichTextBlock rendering.
    /// </summary>
    public sealed class AnsiRichTextFormatter
    {
        private readonly Color _defaultForeground;
        private readonly StringBuilder _textBuffer = new();
        private readonly StringBuilder _escapeBuffer = new();
        private bool _parsingEscape;
        private Color? _foreground;
        private bool _bold;

        private static readonly Color[] BasicPalette =
        {
            Color.FromArgb(255, 0, 0, 0),
            Color.FromArgb(255, 194, 54, 33),
            Color.FromArgb(255, 37, 188, 36),
            Color.FromArgb(255, 173, 173, 39),
            Color.FromArgb(255, 73, 46, 225),
            Color.FromArgb(255, 211, 56, 211),
            Color.FromArgb(255, 51, 187, 200),
            Color.FromArgb(255, 203, 204, 205)
        };

        private static readonly Color[] BrightPalette =
        {
            Color.FromArgb(255, 129, 131, 131),
            Color.FromArgb(255, 252, 57, 31),
            Color.FromArgb(255, 49, 231, 34),
            Color.FromArgb(255, 234, 236, 35),
            Color.FromArgb(255, 88, 51, 255),
            Color.FromArgb(255, 249, 53, 248),
            Color.FromArgb(255, 20, 240, 240),
            Color.FromArgb(255, 233, 235, 235)
        };

        public AnsiRichTextFormatter(Color defaultForeground)
        {
            _defaultForeground = defaultForeground;
        }

        public IReadOnlyList<StyledSegment> Convert(string input)
        {
            var segments = new List<StyledSegment>();

            foreach (var ch in input)
            {
                if (_parsingEscape)
                {
                    _escapeBuffer.Append(ch);
                    if (IsTerminator(ch))
                    {
                        HandleEscapeSequence(_escapeBuffer.ToString());
                        _escapeBuffer.Clear();
                        _parsingEscape = false;
                    }

                    continue;
                }

                if (ch == '\u001b')
                {
                    FlushText(segments);
                    _parsingEscape = true;
                    _escapeBuffer.Clear();
                    continue;
                }

                if (ch == '\r')
                {
                    _textBuffer.Append('\n');
                    continue;
                }

                _textBuffer.Append(ch);
            }

            FlushText(segments);
            return segments;
        }

        private void FlushText(List<StyledSegment> segments)
        {
            if (_textBuffer.Length == 0)
            {
                return;
            }

            var text = _textBuffer.ToString();
            _textBuffer.Clear();

            if (text.Length == 0)
            {
                return;
            }

            segments.Add(new StyledSegment(text, _foreground ?? _defaultForeground, _bold));
        }

        private void HandleEscapeSequence(string sequence)
        {
            if (string.IsNullOrEmpty(sequence))
            {
                return;
            }

            if (sequence[0] == '[')
            {
                var finalChar = sequence[^1];
                if (sequence.Length < 2)
                {
                    return;
                }

                var parameterPart = sequence.Substring(1, sequence.Length - 2);
                if (finalChar == 'm')
                {
                    ApplySgr(parameterPart);
                }
                else if (finalChar == 'K')
                {
                    _textBuffer.Clear();
                }
                else if (finalChar == 'J')
                {
                    _textBuffer.Clear();
                }
            }
        }

        private void ApplySgr(string parameterPart)
        {
            if (string.IsNullOrWhiteSpace(parameterPart))
            {
                ResetStyles();
                return;
            }

            var tokens = parameterPart.Split(';');
            if (tokens.Length == 0)
            {
                ResetStyles();
                return;
            }

            for (int i = 0; i < tokens.Length; i++)
            {
                var code = ParseCode(tokens[i]);
                switch (code)
                {
                    case 0:
                        ResetStyles();
                        break;
                    case 1:
                        _bold = true;
                        break;
                    case 22:
                        _bold = false;
                        break;
                    case 39:
                        _foreground = null;
                        break;
                    case >= 30 and <= 37:
                        _foreground = BasicPalette[code - 30];
                        break;
                    case >= 90 and <= 97:
                        _foreground = BrightPalette[code - 90];
                        break;
                    case 38:
                        if (i + 1 < tokens.Length)
                        {
                            var mode = ParseCode(tokens[++i]);
                            if (mode == 2 && i + 3 < tokens.Length)
                            {
                                var r = ClampByte(ParseCode(tokens[++i]));
                                var g = ClampByte(ParseCode(tokens[++i]));
                                var b = ClampByte(ParseCode(tokens[++i]));
                                _foreground = Color.FromArgb(255, r, g, b);
                            }
                            else if (mode == 5 && i + 1 < tokens.Length)
                            {
                                var idx = ParseCode(tokens[++i]);
                                _foreground = ColorFrom256(idx);
                            }
                        }
                        break;
                }
            }
        }

        private static bool IsTerminator(char c) => c >= '@' && c <= '~';

        private static int ParseCode(string token)
        {
            if (int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                return value;
            }

            return 0;
        }

        private static byte ClampByte(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return (byte)value;
        }

        private static Color ColorFrom256(int index)
        {
            if (index < 0)
            {
                index = 0;
            }

            if (index < 16)
            {
                return index < 8 ? BasicPalette[index] : BrightPalette[index - 8];
            }

            if (index < 232)
            {
                var idx = index - 16;
                var r = idx / 36;
                var g = (idx % 36) / 6;
                var b = idx % 6;
                return Color.FromArgb(255, MapColorComponent(r), MapColorComponent(g), MapColorComponent(b));
            }

            var gray = (byte)(8 + (index - 232) * 10);
            return Color.FromArgb(255, gray, gray, gray);
        }

        private static byte MapColorComponent(int value)
        {
            if (value <= 0)
            {
                return 0;
            }

            return (byte)(55 + value * 40);
        }

        private void ResetStyles()
        {
            _foreground = null;
            _bold = false;
        }
    }

    public readonly record struct StyledSegment(string Text, Color Foreground, bool Bold);
}
