namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// The CVM Debugger's own default test program: the text <see cref="Services.CvmMemoryProtocol.TryBuildDebuggerTestProgram"/>
/// assembles for a fresh debug session, the Assembly Code editor starts with
/// (<see cref="ViewModels.CvmDebuggerViewModel"/>) and a new chip's "default" debugger program is seeded from
/// (<see cref="Models.Ga144ChipConfiguration"/>), so Start and an unedited click of Assemble always produce
/// byte-identical simulated-SRAM contents.
///
/// <b>REPLACED 2026-10-04.</b> The previous text was a ~85-word CVM1-era program (pushlit, lit, usl/ssr/usr, add/sub,
/// enter/stl/ldl, 'br/'cbr exploration, ...) whose opcodes were retired by the 2026-09-30 VM reset and the
/// 2026-10-03 redesign: the retired IDE assembler silently turned most of it into <c>nop</c>s, and the ONE CVM
/// assembler (<see cref="Ga144.Cvm.Toolchain.CvmAssembler"/>, now used by the debugger too) correctly rejects
/// words that no longer exist. The new program is deliberately tiny and uses only words confirmed on real
/// hardware: <c>nop</c>, <c>slit</c>, <c>push</c>, <c>rpop</c>, <c>if</c> (not taken), <c>call</c>/<c>ret</c> and
/// <c>halt</c>. It asserts no expected values; extend it as more of the redesigned VM is hardware-verified. The old
/// text, with all its derivations of expected values, is preserved in the project history.
///
/// Operands are separated by spaces (the documented style); commas are accepted as separators too, in instructions
/// and in <c>.word</c>/<c>.import</c>/<c>.export</c> alike.
/// </summary>
public static class CvmDebuggerDefaultProgram
{
  public const string Source =
      "; CVM Debugger default program -- uses only words already confirmed on real hardware.\n" +
      "; Operands are separated by spaces (commas are accepted as separators too).\n" +
      "start:\n" +
      "  nop\n" +
      "  slit 100            ; push the 11-bit signed literal 100 onto the data stack\n" +
      "  push 0x1234         ; push a full 16-bit literal (opcode word + literal word)\n" +
      "  rpop r1             ; r1 = 0x1234\n" +
      "  rpop r0             ; r0 = 100\n" +
      "  if r0 ==0 then skip ; r0 is not 0, so this branch is NOT taken\n" +
      "  nop\n" +
      "skip:\n" +
      "  call subr           ; call/ret round trip\n" +
      "  halt                ; stop here\n" +
      "subr:\n" +
      "  nop\n" +
      "  ret\n";
}