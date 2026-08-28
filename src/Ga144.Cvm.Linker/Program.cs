// galink -- the CVM linker command-line front end.
//
// NOT YET IMPLEMENTED. This project is scaffolded now (so the solution's overall shape -- assembler,
// librarian, linker, all three referencing the shared Ga144.Cvm.Toolchain library -- is in place) but
// the actual linking engine is a follow-up piece of work. It needs one more piece that doesn't exist
// yet either: a small "primitive table" file the IDE exports after compiling node 607, mapping each
// built-in CVM instruction mnemonic (nop/pushlit/push/pop) to the numeric opcode (0x8000 | wordAddress)
// that THIS build of node 607's interpreter actually uses for it -- opcode binding was chosen to be
// fully resolved at link time, so galink needs that snapshot to turn CvmOpcode relocations into real
// numbers; a stale snapshot (node 607 recompiled since) would silently link a program against the
// wrong addresses, so the plan is for the IDE to stamp a build identifier into that file and for
// galink to at least warn if it looks stale.
//
// Planned shape: given one or more .gaobj files (plus optional -l <archive.galib> library search
// paths, resolved after the direct inputs, same convention as a traditional linker), a memory layout
// (which section starts at which page:address -- CODE at 0:0000 by default), and the primitive table,
// galink resolves every symbol (reporting genuinely undefined ones as link errors), applies every
// relocation now that final addresses are known, and writes a single flat .gaimg -- the "WORD" chunk
// of which is exactly the List<int> program CvmSimulatedSram.LoadProgram / the real-hardware
// installer already know how to consume today, plus a "SYMT" chunk of the final resolved addresses
// so the CVM Debugger can eventually label a linked user program's own addresses, not just node 607's
// interpreter internals.
//   galink <input1.gaobj> [input2.gaobj ...] --primitives <node607.gaprim> -o <output.gaimg>
if (args.Length == 0 || args[0] is "-h" or "--help")
{
  PrintUsage();
  return args.Length == 0 ? 1 : 0;
}

Console.Error.WriteLine("galink: linking is not implemented yet -- see this project's Program.cs for the planned design.");
return 1;

static void PrintUsage()
{
  Console.WriteLine("""
      galink -- CVM linker (not yet implemented)

      Planned usage:
        galink <input1.gaobj> [input2.gaobj ...] [-l <archive.galib> ...] --primitives <node607.gaprim> -o <output.gaimg>
      """);
}
