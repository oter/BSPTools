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
        private static readonly Regex VECTOR_TABLE_START = new Regex(@"^.+g_pfnVectors\[\]", RegexOptions.Singleline);
        private static readonly Regex VECTOR_TABLE_CLOSE = new Regex(@"^[ \t]{0,}\};", RegexOptions.Singleline);
        private static readonly Regex VECTOR_TABLE_ITEM = new Regex(@"^[ \t]{0,}[a-zA-Z0-9_]+", RegexOptions.Singleline);
        private static readonly Regex VECTOR_TABLE_ITEM_NAME = new Regex(@"[a-zA-Z0-9_]+", RegexOptions.Singleline);
        private static readonly Regex VECTOR_TABLE_ITEM_COMMENT = new Regex(@"//.+", RegexOptions.Singleline);
        private const string UNINTIALIZED_VECTOR_ENTRY = "0";

        public static StartupFileGenerator.InterruptVectorTable Parse(string mcuFamilyName, string searchDir, string startupFileName, Dictionary<string, string> systemVars)
        {
            var startupFiles = new DirectoryInfo(searchDir).GetFiles(startupFileName, SearchOption.AllDirectories);

            if (startupFiles.Length != 1)
            {
                throw new Exception("More than one or no startup files found!");
            }

            var vectorTable = ParseInterruptVectors(startupFiles[0].FullName);

            return new StartupFileGenerator.InterruptVectorTable
            {
                FileName = Path.GetFileName(startupFileName),
                MatchPredicate = m => true,
                Vectors = vectorTable,
                StartupFileTemplatePath = Path.GetFullPath(systemVars["$$BSPGEN:RULES_DIR$$"] + @"\StartupFileTemplate.c")
            };
        }

        private static StartupFileGenerator.InterruptVector[] ParseInterruptVectors(string fullName)
        {
            var numOfReservedInterrupts = 0;
            var vectors = new List<StartupFileGenerator.InterruptVector>();
            var estackVector = new StartupFileGenerator.InterruptVector
            {
                Name = "_estack",
                OptionalComment = ""
            };
            vectors.Add(estackVector);
            Match match = null;
            bool startFound = false;

            foreach (var line in File.ReadLines(fullName))
            {
                if (!startFound)
                {
                    if (VECTOR_TABLE_START.Match(line).Success)
                    {
                        startFound = true;
                    }
                }
                else
                {
                    if (VECTOR_TABLE_CLOSE.Match(line).Success)
                    {
                        break;
                    }
                    match = VECTOR_TABLE_ITEM.Match(line);
                    if (!match.Success)
                    {
                        continue;
                    }
                    match = VECTOR_TABLE_ITEM_NAME.Match(line);
                    if (!match.Success)
                    {
                        continue;
                    }
                    var vectorName = match.ToString();
                    if (vectorName == UNINTIALIZED_VECTOR_ENTRY)
                    {
                        vectorName = string.Format("Reserved_{0}_Handler", numOfReservedInterrupts++);
                    }
                    var comment = "";
                    match = VECTOR_TABLE_ITEM_COMMENT.Match(line);
                    if (match.Success)
                    {
                        comment = match.ToString().Substring(2);
                    }
                    var vector = new StartupFileGenerator.InterruptVector
                    {
                        Name = vectorName,
                        OptionalComment = comment
                    };
                    vectors.Add(vector);
                }
            }

            if (vectors.Count < 2)
            {
                throw new Exception("Incorrect vector list.");
            }
            if (vectors[1].Name != "ResetISR")
            {
                throw new Exception("ResetISR vector name requiered at first index.");
            }
            vectors[1].Name = "Reset_Handler";      

            return vectors.ToArray();
        }
    }
}
