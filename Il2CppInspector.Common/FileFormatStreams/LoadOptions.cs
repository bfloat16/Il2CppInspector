/*
    Copyright 2017-2021 Katy Coe - http://www.djkaty.com - https://github.com/djkaty

    All rights reserved.
*/

namespace Il2CppInspector
{
    // Modifiers for use when loading binary files
    public class LoadOptions
    {
        // For ELF files, the virtual address to which we should rebase - ignored for other file types
        // Use zero to prevent rebasing
        public ulong ImageBase { get; set; } = 0ul;

        // Optional auxiliary metadata; the selected plugin can discover it beside global metadata.
        public string StartupMetadataPath { get; set; }

        // NAME_REGION_PLATFORM_VERSION. Null selects an installed plugin automatically or uses the stock reader.
        public string Game { get; set; }

        public Action<OperationProgress> ProgressCallback { get; set; }
    }
}
