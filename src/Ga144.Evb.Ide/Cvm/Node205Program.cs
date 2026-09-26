namespace Ga144.Evb.Ide.Cvm;

/// <summary>
/// Node 205's resident F18 source -- CVM2's floating-point subprocessor, step 1 (unpack and categorize).
/// Supplied by Stefan 2026-09-26 as part of the complete FP subprocessor rewrite (Node 102's own remarks give the full context: Stefan supplied the complete, hardware-working FP subprocessor rewrite on 2026-09-26, spanning nodes 501-504, 401-404, 301-306, 201-205 and 102-105; nodes 601-604 are explicitly retired from this role.).
///
/// Reads two raw IEEE-754 32-bit floats (as <c>l,h</c> word pairs) from node 305 (the register file) and
/// decomposes each into type, sign, effective exponent, mantissa-high and mantissa-low, forwarding the
/// combined 11-word packet on to node 204. <c>f1/type</c> classifies a value as finite nonzero, zero,
/// infinity, qNaN or sNaN by testing the raw 8-bit exponent field and, when it is all-ones, the mantissa's
/// bit 22 as the quiet/signaling distinguisher. <c>f1/decompose</c> additionally computes the effective
/// (subnormal-adjusted) exponent and inserts the implicit leading-1 mantissa bit for normal numbers only,
/// leaving it unset for zero, subnormal, infinity and NaN.
///
/// This is a brand new coordinate for this project -- no earlier revision of node 205 existed here.
/// </summary>
internal static class Node205Program
{
  /// <summary>The node this program is always deployed to -- CVM2's floating-point subprocessor, step 1 (unpack and categorize incoming operands). Added 2026-09-26.</summary>
  public const int Coordinate = 205;

  /// <summary>
  /// Node 205's full resident F18 source, verbatim from Stefan's 2026-09-26 paste.
  /// </summary>
  public const string Source = """
      ( CVM2 node 205. VM 32 bit floatingpoint step 1. unpack & categorize

        receives 2 floatingpoint numbers and decomposes each into

        1. type:
            t = 0x00   finite nonzero
            t = 0x01   zero
            t = 0x02   infinity
            t = 0x04   qNaN
            t = 0x0c   sNaN       // NAN bit + signaling bit

        2. sign:
             0       => positive
             0x8000  => negative

        3. exponent:
             effective IEEE-754 biased exponent
             normal      = raw exponent
             subnormal   = 1
             zero        = 0
             inf/NaN     = 0xff

        4. mantissa high:
             normal numbers have implicit leading 1 made explicit in bit 7

        5. mantissa low

        in:    o l1 h1 l2 h2                     {from node 305}
        out:   o t1 s1 e1 h1 l1 t2 s2 e2 h2 l2   {to node 204}
      )

      # 0 org
      # down /b
      # right /a
      entry f1/main


      : f1/frac? ( l h - l h f )
        dup 0x7f and
        if
          ;
        then
        drop
        over
      ;


      // leaves l h intact and adds type

      : f1/type ( l h - l h t )

        dup
        2/ 2/ 2/ 2/ 2/ 2/ 2/
        0xff and

        // exponent != 0

        if
          0xff xor ..

          // exponent 1..254 -> finite nonzero

          if
            dup
            xor
            ;
          then

          // exponent == 0xff

          drop
          f1/frac?

          // fraction != 0 -> NaN

          if
            drop

            // fraction bit 22 is quiet bit

            dup 0x40 and
            if
              drop
              0x04
              ;
            then

            drop
            0x0c
            ;
          then

          // exponent ff, fraction zero -> infinity

          drop
          0x02
          ;
        then

        // exponent == 0

        drop
        f1/frac?

        // nonzero fraction -> subnormal finite

        if
          dup
          xor
          ;
        then

        // exponent zero, fraction zero -> zero

        drop
        0x01
      ;


      : f1/decompose

        @b
        ( l h )
        // type

        f1/type
        !

        // sign

        dup 0x8000 and
        !

        // raw exponent

        dup
        2/ 2/ 2/ 2/ 2/ 2/ 2/
        0xff and

        // Keep raw exponent for deciding whether the
        // implicit mantissa bit must be inserted.

        dup >r

        // Output effective exponent:
        //
        // normal       raw exponent
        // subnormal    1
        // zero         0
        // inf/nan      ff

        if
          !
        else
          drop
          f1/frac?
          if
            drop 1
          else
            dup xor
          then
          !
        then

        // Use the original raw exponent here.
        // Subnormals must NOT receive the implicit bit.

        r>

        if
          0xff xor ..
          if
            drop
            0x7f and
            0x80 xor
            !                    // h
            !                    // l
            ;
          then
        then

        // zero, subnormal, infinity or NaN

        drop
        0x7f and
        !                        // h
        !                        // l
      ;


      : f1/main
        @b !                     // o

        @b f1/decompose             // l1 h1 -> t1 s1 e1 h1 l1
        @b f1/decompose             // l2 h2 -> t2 s2 e2 h2 l2

        f1/main
      ;
      """;
}
