.text
.globl _start
_start:
  .space 0x20, 0x90
.globl fixture_function
fixture_function:
  xorps %xmm0, %xmm0
  ret
  .space 0x100, 0x90
.data
  .space 0x100, 0
