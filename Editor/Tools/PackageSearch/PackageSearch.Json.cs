// Package Search: a minimal JSON reader for VPM listings and package.json files. Full docs: CLAUDE.md > Tools > Package Search.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HaTools
{
    public partial class PackageSearch
    {
        // JsonUtility can't read objects keyed by name (a listing's "packages" and "versions"), so this reads any JSON into
        // Dictionary<string, object> (objects), List<object> (arrays), string, double, bool and null
        internal static class Json
        {
            internal static object Parse(string text)
            {
                if (text == null) throw new FormatException("No JSON");
                int i = 0;
                object value = Value(text, ref i);
                Skip(text, ref i);
                if (i != text.Length) throw Error(text, i);
                return value;
            }

            internal static Dictionary<string, object> Obj(object o, string key) =>
                o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as Dictionary<string, object> : null;

            internal static string Str(object o, string key) =>
                o is Dictionary<string, object> d && d.TryGetValue(key, out var v) ? v as string : null;

            static object Value(string s, ref int i)
            {
                Skip(s, ref i);
                if (i >= s.Length) throw Error(s, i);
                char c = s[i];
                if (c == '{') return Object(s, ref i);
                if (c == '[') return Array(s, ref i);
                if (c == '"') return String(s, ref i);
                if (Word(s, ref i, "true")) return true;
                if (Word(s, ref i, "false")) return false;
                if (Word(s, ref i, "null")) return null;
                return Number(s, ref i);
            }

            static Dictionary<string, object> Object(string s, ref int i)
            {
                var d = new Dictionary<string, object>();
                i++; // {
                Skip(s, ref i);
                if (i < s.Length && s[i] == '}') { i++; return d; }
                while (true)
                {
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != '"') throw Error(s, i);
                    string key = String(s, ref i);
                    Skip(s, ref i);
                    if (i >= s.Length || s[i] != ':') throw Error(s, i);
                    i++;
                    d[key] = Value(s, ref i);
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return d; }
                    throw Error(s, i);
                }
            }

            static List<object> Array(string s, ref int i)
            {
                var list = new List<object>();
                i++; // [
                Skip(s, ref i);
                if (i < s.Length && s[i] == ']') { i++; return list; }
                while (true)
                {
                    list.Add(Value(s, ref i));
                    Skip(s, ref i);
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return list; }
                    throw Error(s, i);
                }
            }

            static string String(string s, ref int i)
            {
                var sb = new StringBuilder();
                i++; // opening quote
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }
                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case '"': case '\\': case '/': sb.Append(e); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            if (i + 4 > s.Length || !ushort.TryParse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort u))
                                throw Error(s, i);
                            sb.Append((char)u);
                            i += 4;
                            break;
                        default: throw Error(s, i - 1);
                    }
                }
                throw Error(s, i);
            }

            static double Number(string s, ref int i)
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                if (i == start || !double.TryParse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double n))
                    throw Error(s, start);
                return n;
            }

            static bool Word(string s, ref int i, string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            static void Skip(string s, ref int i)
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
                if (i == 0 && s.Length > 0 && s[0] == '﻿') { i++; Skip(s, ref i); } // byte order mark
            }

            static FormatException Error(string s, int i) => new FormatException($"Invalid JSON at character {i} of {s.Length}");
        }
    }
}
