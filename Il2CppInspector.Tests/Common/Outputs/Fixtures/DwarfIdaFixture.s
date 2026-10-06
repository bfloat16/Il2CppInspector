.text
.global _start
.type _start, %function
_start:
    ret
    .space 28
    ret
    .space 12
    ret
    .space 12
    ret
    .space 12
    ret
.size _start, .-_start

.data
.balign 16
.space 256

.bss
.balign 16
.space 64
