/* Copyright (c) 2016 Sysprogs OU. All Rights Reserved.
   This software is licensed under the Sysprogs BSP Generator License.
   https://github.com/sysprogs/BSPTools/blob/master/LICENSE
*/

using BSPGenerationTools;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace cc26xx_bsp_generator
{
    internal class StartupFilesParser
    {
        private static readonly Regex VECTOR_TABLE_ITEM = new Regex(@"^((static void)|(void))[ \t]+[a-zA-Z0-9_ ]+\([ \t]*void[ \t]*\)[a-zA-Z0-9_\(\) ]*;", RegexOptions.Singleline);
        private static readonly Regex VECTOR_TABLE_ITEM_NAME = new Regex(@"[a-zA-Z_][a-zA-Z0-9_]*", RegexOptions.Singleline);
        private static readonly string STATIC_MODIFIER = @"static";
        private static readonly string VOID_TYPE = @"void";

        public static StartupFileGenerator.InterruptVectorTable Parse(string mcuFamilyName, string searchDir, string startupFileName)
        {
            var startupFiles = new DirectoryInfo(searchDir).GetFiles(startupFileName, SearchOption.AllDirectories);

            if (startupFiles.Length > 1)
            {
                throw new Exception("More than one startup files found!");
            }

            if (startupFiles.Length != 1)
            {
                throw new Exception("Startup file not found!");
            }

            var vectorTable = ParseInterruptVectors(startupFiles[0].FullName);

            return new StartupFileGenerator.InterruptVectorTable
            {
                FileName = Path.GetFileName(startupFileName),
                MatchPredicate = m => true,
                Vectors = vectorTable
            };
        }

        private static StartupFileGenerator.InterruptVector[] ParseInterruptVectors(string fullName)
        {
            var vectors = new List<StartupFileGenerator.InterruptVector>();
            var estackVector = new StartupFileGenerator.InterruptVector
            {
                Name = "_estack",
                OptionalComment = ""
            };
            vectors.Add(estackVector);
            Match match = null;

            foreach (var line in File.ReadLines(fullName))
            {
                match = VECTOR_TABLE_ITEM.Match(line);
                if (!match.Success)
                {
                    continue;
                }

                var matches = VECTOR_TABLE_ITEM_NAME.Matches(line);

                foreach (var m in matches)
                {
                    var name = m.ToString();
                    if (!name.Equals(STATIC_MODIFIER) && !name.Equals(VOID_TYPE))
                    {
                        var vector = new StartupFileGenerator.InterruptVector
                        {
                            Name = m.ToString(),
                            OptionalComment = @""
                        };
                        vectors.Add(vector);
                        break;
                    }
                }
            }

            return vectors.ToArray();
        }
    }
}
