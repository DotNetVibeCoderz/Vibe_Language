using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace BosoEngine.Engine
{
    /// <summary>
    /// Transpiler sederhana dari Boso Script ke C#.
    /// Validasi dilakukan dengan Roslyn (Microsoft.CodeAnalysis).
    /// </summary>
    public class BosoTranspiler
    {
        // Peta keyword Boso -> C#
        private readonly Dictionary<string, string> _keywordMap = new()
        {
            {"jika", "if"},
            {"lain", "else"},
            {"untuk", "for"},
            {"selama", "while"},
            {"kembali", "return"},
            {"kelas", "class"},
            {"publik", "public"},
            {"privat", "private"},
            {"statis", "static"},
            {"menggunakan", "using"},
            {"baru", "new"},
            {"string", "string"},
            {"angka", "int"},
            {"desimal", "double"},
            {"boolean", "bool"},
            {"benar", "true"},
            {"salah", "false"},
            {"tulis", "Console.WriteLine"},
			{"Utama", "Main"}
		};

        public string TranspileToCSharp(string bosoCode)
        {
            if (string.IsNullOrWhiteSpace(bosoCode))
                throw new ArgumentException("Boso script is empty.");

            string result = bosoCode;

            // Replace keywords dengan regex word-boundary
            foreach (var kv in _keywordMap)
            {
                result = Regex.Replace(result, $@"\b{Regex.Escape(kv.Key)}\b", kv.Value);
            }

            // Auto-inject using System; jika ada Console.WriteLine
            if (result.Contains("Console.WriteLine") && !result.Contains("using System"))
            {
                result = "using System;\n" + result;
            }

            ValidateCSharp(result);
            return result;
        }

        private void ValidateCSharp(string csharpCode)
        {
            var tree = CSharpSyntaxTree.ParseText(csharpCode);
            var errors = new List<Diagnostic>();
            foreach (var diag in tree.GetDiagnostics())
            {
                if (diag.Severity == DiagnosticSeverity.Error)
                {
                    errors.Add(diag);
                }
            }

            if (errors.Count > 0)
            {
                throw new Exception("Validation failed: " + string.Join("; ", errors));
            }
        }
    }
}
