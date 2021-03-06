﻿/* Copyright (c) 2015 Sysprogs OU. All Rights Reserved.
   This software is licensed under the Sysprogs BSP Generator License.
   https://github.com/sysprogs/BSPTools/blob/master/LICENSE
*/

using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Linq;
using System.Threading;

namespace LinkerScriptGenerator
{
    public class LdsFileGenerator : IDisposable
    {
        LinkerScriptTemplate _ScriptTemplate;
        MemoryLayout _MemoryTemplate;

        Memory _MainFLASH;
        Memory _MainRAM;

        Dictionary<string, Memory> _Memories = new Dictionary<string, Memory>();

        public bool RedirectMainFLASHToRAM;
        public bool GenerateSectionsForUnreferencedMemories = true;

        public LdsFileGenerator(LinkerScriptTemplate scriptTemplate, MemoryLayout memoryTemplate)
        {
            _ScriptTemplate = scriptTemplate;
            _MemoryTemplate = memoryTemplate;

            if (memoryTemplate.Memories == null)
                throw new Exception("Memory list cannot be empty");
            foreach (var mem in memoryTemplate.Memories)
            {
                _Memories[mem.Name] = mem;
                if (mem.Type == MemoryType.FLASH)
                {
                    if (_MainFLASH == null)
                        _MainFLASH = mem;
                    if (mem.Access == MemoryAccess.Undefined)
                        mem.Access = MemoryAccess.Readable | MemoryAccess.Executable;
                }
                if (mem.Type == MemoryType.RAM)
                {
                    if (_MainRAM == null)
                        _MainRAM = mem;
                    if (mem.Access == MemoryAccess.Undefined)
                        mem.Access = MemoryAccess.Readable | MemoryAccess.Writable | MemoryAccess.Executable;
                }

                // Main flash can be 0!
                if (mem.Size == 0 && ((_MainFLASH == null) || (mem.MemoryDefinitionString != _MainFLASH.MemoryDefinitionString)))
                    throw new Exception("Memory size cannot be 0");
            }

            if (_MainRAM == null)
                throw new Exception("Cannot find the primary RAM memory. At least one memory with Type = RAM should be defined.");
            if (_MainFLASH == null)
            {
                _MainFLASH = _MainRAM;
                //throw new Exception("Cannot find the primary FLASH memory. At least one memory with Type = FLASH should be defined.");
            }
        }

        public void GenerateLdsFile(StreamWriter sw, IEnumerable<string> extraLines = null)
        {
            sw.WriteLine("/* Generated by LinkerScriptGenerator [http://visualgdb.com/tools/LinkerScriptGenerator]");
            sw.WriteLine(" * Target: " + _MemoryTemplate.DeviceName);
            sw.WriteLine(" * The file is provided under the BSD license.");
            sw.WriteLine(" */");
            sw.WriteLine("");

            if (string.IsNullOrEmpty(_ScriptTemplate.EntryPoint))
                throw new Exception("Entry Point should be defined in the script template");

            if (extraLines != null)
                foreach (var line in extraLines)
                    sw.WriteLine(line);

            sw.WriteLine("ENTRY({0})", _ScriptTemplate.EntryPoint);
            sw.WriteLine("", _ScriptTemplate.EntryPoint);

            OutputMemoryDefinition(sw);
            OutputSectionDefinitions(sw);
        }

        private void OutputSectionDefinitions(StreamWriter sw)
        {
            sw.WriteLine("SECTIONS");
            sw.WriteLine("{");
            Dictionary<string, bool> referencedMemories = new Dictionary<string, bool>();

            string initializedDataSectionLabel = null;

            foreach (var section in _ScriptTemplate.Sections)
            {
                if ((section.Flags & SectionFlags.InitializerInMainMemory) != SectionFlags.None)
                {
                    string nameAsID = section.Name.TrimStart('.').Replace('.', '_');
                    initializedDataSectionLabel = "_si" + nameAsID;
                }
            }

            for (int i = 0; i < _ScriptTemplate.Sections.Count; i++)
            {
                var section = _ScriptTemplate.Sections[i];
                
                OutputSectionDefinition(sw, "\t", section);
                referencedMemories[section.TargetMemory] = true;

                if (initializedDataSectionLabel != null && i < (_ScriptTemplate.Sections.Count - 1))
                {
                    bool lastFLASHSection = (section.TargetMemory == _MainFLASH.Name) && (_ScriptTemplate.Sections[i + 1].TargetMemory != _MainFLASH.Name);
                    bool nextSectionIsMainDataSection = (_ScriptTemplate.Sections[i+1].Flags & SectionFlags.InitializerInMainMemory) != SectionFlags.None;
                    if ((lastFLASHSection || nextSectionIsMainDataSection) && !RedirectMainFLASHToRAM)
                    {
                        sw.WriteLine("{0}. = ALIGN(4);", "\t");   //Align initializer data to DWORD boundary as it's copied DWORD-by-DWORD

                        OutputDefinition(sw, initializedDataSectionLabel, ".", "\t");
                        initializedDataSectionLabel = null;
                    }
                }
            }

            sw.WriteLine("\tPROVIDE(end = .);");
            sw.WriteLine("");

            foreach (var kv in _Memories)
            {
                if (kv.Value == _MainFLASH || kv.Value == _MainRAM)
                    continue;
                if (referencedMemories.ContainsKey(kv.Key))
                    continue;

                string baseName = "." + kv.Key.ToLower() + "_text";

                var section = new Section
                {
                    TargetMemory = kv.Key,
                    Flags = SectionFlags.DefineShortLabels,
                    Name = baseName,
                    Inputs = new List<SectionReference> { new SectionReference { Flags = SectionReferenceFlags.AddPrefixForm } }
                };

                //This is currently not supported until we decide how to initialize those memories based on customer feedback
                //OutputSectionDefinition(sw, "\t", section);
            }


            if (_ScriptTemplate.SectionsAfterEnd != null)
            {
                foreach (var section in _ScriptTemplate.SectionsAfterEnd)
                    OutputSectionDefinition(sw, "\t", section);
            }

            sw.WriteLine("}");
            sw.WriteLine("");
        }

        void OutputSectionDefinition(StreamWriter sw, string padding, Section section)
        {
            string nameAsID = section.Name.TrimStart('.').Replace('.', '_');

            string targetMemory;
            if (RedirectMainFLASHToRAM && section.TargetMemory == _MainFLASH.Name)
                targetMemory = _MainRAM.Name;
            else
                targetMemory = section.TargetMemory;

            if (_Memories.ContainsKey(targetMemory))
            {
                string loadBase = null;
                if ((section.Flags & SectionFlags.InitializerInMainMemory) != SectionFlags.None && !RedirectMainFLASHToRAM)
                {
                    loadBase = "_si" + nameAsID;
                    //OutputDefinition(sw, loadBase, ".", padding);
                }

                string modifiers = "";
                if ((section.Flags & SectionFlags.NoLoad) != SectionFlags.None)
                    modifiers += " (NOLOAD)";

                if (loadBase != null)
                    sw.WriteLine("{0}{1}{2} : AT({3})", padding, section.Name, modifiers, loadBase);
                else
                    sw.WriteLine("{0}{1}{2} :", padding, section.Name, modifiers);

                sw.WriteLine(padding + "{");

                int alignment = _ScriptTemplate.SectionAlignment;
                if (section.Alignment != 0)
                    alignment = section.Alignment;

                if (alignment != 0 && !section.IsUnaligned)
                    sw.WriteLine("{0}\t. = ALIGN({1});", padding, alignment);

                string startLabel = ".";

                if ((section.Flags & SectionFlags.InitializerInMainMemory) != SectionFlags.None && RedirectMainFLASHToRAM)
                    startLabel = OutputDefinition(sw, "_si" + nameAsID, startLabel, padding + "\t");

                if ((section.Flags & SectionFlags.DefineShortLabels) != SectionFlags.None)
                    startLabel = OutputDefinition(sw, "_s" + nameAsID, startLabel, padding + "\t");
                if ((section.Flags & SectionFlags.ProvideLongLabels) != SectionFlags.None)
                    sw.WriteLine("{0}\tPROVIDE(__{1}_start__ = {2});", padding, nameAsID, startLabel);
                if ((section.Flags & SectionFlags.ProvideLongLabelsLeadingUnderscores) != SectionFlags.None)
                    sw.WriteLine("{0}\tPROVIDE(__{1}_start = {2});", padding, nameAsID, startLabel);
                if ((section.Flags & SectionFlags.DefineMediumLabels) != SectionFlags.None)
                    sw.WriteLine("{0}\t_{1}_start = {2};", padding, nameAsID, startLabel);
                if (!string.IsNullOrEmpty(section.CustomStartLabel))
                    sw.WriteLine("{0}\t{1} = {2};", padding, section.CustomStartLabel, startLabel);

                if (section.Inputs != null)
                {
                    foreach (var input in section.Inputs)
                    {
                        string namePattern = input.NamePattern;
                        if (string.IsNullOrEmpty(namePattern))
                            namePattern = section.Name;
                        OutputSectionReference(sw, padding + "\t", namePattern, input.Flags);
                    }
                }

                if (section.CustomContents != null)
                {
                    foreach (var line in section.CustomContents)
                        sw.WriteLine(padding + "\t" + line);
                }

                if (section.Fill != null)
                {
                    sw.WriteLine("{0}\tFILL(0x{1:X8});", padding, section.Fill.Pattern);
                    sw.WriteLine("{0}\t. = 0x{1:x};", padding, section.Fill.TotalSize);
                }

                if (alignment != 0 && !section.IsUnaligned)
                    sw.WriteLine("{0}\t. = ALIGN({1});", padding, alignment);

                string endLabel = ".";

                if ((section.Flags & SectionFlags.DefineShortLabels) != SectionFlags.None)
                    endLabel = OutputDefinition(sw, "_e" + nameAsID, ".", padding + "\t");
                if ((section.Flags & SectionFlags.ProvideLongLabels) != SectionFlags.None)
                    sw.WriteLine("{0}\tPROVIDE(__{1}_end__ = {2});", padding, nameAsID, endLabel);
                if ((section.Flags & SectionFlags.ProvideLongLabelsLeadingUnderscores) != SectionFlags.None)
                    sw.WriteLine("{0}\tPROVIDE(__{1}_end = {2});", padding, nameAsID, endLabel);
                if ((section.Flags & SectionFlags.DefineMediumLabels) != SectionFlags.None)
                    sw.WriteLine("{0}\t_{1}_end = {2};", padding, nameAsID, endLabel);
                if (!string.IsNullOrEmpty(section.CustomEndLabel))
                    sw.WriteLine("{0}\t{1} = {2};", padding, section.CustomEndLabel, endLabel);

                sw.WriteLine(padding + "}" + " > " + targetMemory);
            }
            else
                sw.WriteLine(padding + "# {0} not defined in {1}", targetMemory, _MemoryTemplate.DeviceName);

            sw.WriteLine("");
        }

        private void OutputSectionReference(StreamWriter sw, string padding, string namePattern, SectionReferenceFlags flags)
        {
            string baseRef = namePattern;
            if ((flags & SectionReferenceFlags.DotPrefixForm) != SectionReferenceFlags.None)
                baseRef += ".*";
            else if ((flags & SectionReferenceFlags.PrefixFormOnly) != SectionReferenceFlags.None)
                baseRef += "*";

            if ((flags & SectionReferenceFlags.Sort) != SectionReferenceFlags.None)
                baseRef = "SORT(" + baseRef + ")";

            string finalRef;
            if ((flags & SectionReferenceFlags.NoBrackets) != SectionReferenceFlags.None)
                finalRef = "*" + baseRef;
            else
                finalRef = "*(" + baseRef + ")";

            if ((flags & SectionReferenceFlags.Keep) != SectionReferenceFlags.None)
                finalRef = "KEEP(" + finalRef + ")";

            sw.WriteLine(padding + finalRef);

            if (((flags & SectionReferenceFlags.AddPrefixForm) != SectionReferenceFlags.None) &&
                ((flags & SectionReferenceFlags.PrefixFormOnly) == SectionReferenceFlags.None))
                OutputSectionReference(sw, padding, namePattern, flags | SectionReferenceFlags.PrefixFormOnly);
        }

        private void OutputMemoryDefinition(StreamWriter sw)
        {
            sw.WriteLine("MEMORY");
            sw.WriteLine("{");

            int maxLen = (from m in _Memories.Values select m.MemoryDefinitionString.Length).Max();

            foreach (var mem in _Memories.Values)
            {
                var str = mem.MemoryDefinitionString;
                sw.WriteLine("\t{0}{1} : ORIGIN = 0x{2:x8}, LENGTH = {3}", mem.MemoryDefinitionString, new string(' ', maxLen - str.Length), mem.Start, mem.SizeWithSuffix);
            }
            sw.WriteLine("}");
            sw.WriteLine("");
            OutputDefinition(sw, "_estack", string.Format("0x{0:x8}", _MainRAM.End));
        }

        string OutputDefinition(StreamWriter sw, string name, string value, string padding = "")
        {
            sw.WriteLine("{2}{0} = {1};", name, value, padding);
            sw.WriteLine("");
            return name;
        }

        public void Dispose()
        {
        }
    }
}
