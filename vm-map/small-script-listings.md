# Smallest scripts — full pseudo-listings (opcodes NOT yet decoded)

### INIT.BIN (64 bytes)
```
file        INIT.BIN
body        1 dwords (4 bytes)
header      F0=0x1 F1=1 F2=0x1 F3=0x1 F4=1 F5=0x1 F6=0x1c
code        [0x00000 .. 0x00001)  1 dwords
table T1    off 0x00001  count 0      tag 0x71  purity 0/0
table T2    off 0x00001  count 0      tag 0x03  purity 0/0
table T3    off 0x00001  count 0      tag 0x8f  purity 0/0
strings     0 inline, 0 references

; ---- CODE (chunked by T3 line-index; opcodes not yet decoded) ----
  0x00000:           2

; ---- TABLES ----
T1 (count 0, off 0x00001): 
T2 (count 0, off 0x00001): 
T3 (count 0, off 0x00001): 

; ---- STRINGS ----
  (no inline strings)
```

### ADDILL.BIN (104 bytes)
```
file        ADDILL.BIN
body        11 dwords (44 bytes)
header      F0=0x1 F1=1 F2=0x1 F3=0x1 F4=1 F5=0x1 F6=0x1c
code        [0x00000 .. 0x00009)  9 dwords
table T1    off 0x00009  count 0      tag 0x71  purity 0/0
table T2    off 0x00009  count 2      tag 0x03  purity 2/2
table T3    off 0x0000b  count 0      tag 0x8f  purity 0/0
strings     0 inline, 0 references

; ---- CODE (chunked by T3 line-index; opcodes not yet decoded) ----
  0x00000:           1f4 3 0 329e 3 0 329d 1f5 2

; ---- TABLES ----
T1 (count 0, off 0x00009): 
T2 (count 2, off 0x00009): 1 4
T3 (count 0, off 0x0000b): 

; ---- STRINGS ----
  (no inline strings)
```

### RTN_B002.BIN (160 bytes)
```
file        RTN_B002.BIN
body        25 dwords (100 bytes)
header      F0=0x1 F1=1 F2=0x1 F3=0x2 F4=1 F5=0x1 F6=0x1c
code        [0x00000 .. 0x00019)  25 dwords
table T1    off 0x00019  count 0      tag 0x71  purity 0/0
table T2    off 0x00019  count 0      tag 0x03  purity 0/0
table T3    off 0x00019  count 0      tag 0x8f  purity 0/0
strings     0 inline, 0 references

; ---- CODE (chunked by T3 line-index; opcodes not yet decoded) ----
  0x00000:           a0 3 cca45 0 ffffffff 0 18 61 c 0 3 52289 3 152616 55 c 0 0 b 55 3 eff77 0 1 2

; ---- TABLES ----
T1 (count 0, off 0x00019): 
T2 (count 0, off 0x00019): 
T3 (count 0, off 0x00019): 

; ---- STRINGS ----
  (no inline strings)
```

### RTN_B003.BIN (160 bytes)
```
file        RTN_B003.BIN
body        25 dwords (100 bytes)
header      F0=0x1 F1=1 F2=0x1 F3=0x2 F4=1 F5=0x1 F6=0x1c
code        [0x00000 .. 0x00019)  25 dwords
table T1    off 0x00019  count 0      tag 0x71  purity 0/0
table T2    off 0x00019  count 0      tag 0x03  purity 0/0
table T3    off 0x00019  count 0      tag 0x8f  purity 0/0
strings     0 inline, 0 references

; ---- CODE (chunked by T3 line-index; opcodes not yet decoded) ----
  0x00000:           a0 3 cca3a 0 ffffffff 0 18 61 c 0 3 52289 3 152616 55 c 0 0 0 55 3 eff77 0 1 2

; ---- TABLES ----
T1 (count 0, off 0x00019): 
T2 (count 0, off 0x00019): 
T3 (count 0, off 0x00019): 

; ---- STRINGS ----
  (no inline strings)
```

### DEBUGANIME.BIN (164 bytes)
```
file        DEBUGANIME.BIN
body        26 dwords (104 bytes)
header      F0=0x1 F1=1 F2=0x1 F3=0x1 F4=1 F5=0x1 F6=0x1c
code        [0x00000 .. 0x0001a)  26 dwords
table T1    off 0x0001a  count 0      tag 0x71  purity 0/0
table T2    off 0x0001a  count 0      tag 0x03  purity 0/0
table T3    off 0x0001a  count 0      tag 0x8f  purity 0/0
strings     0 inline, 0 references

; ---- CODE (chunked by T3 line-index; opcodes not yet decoded) ----
  0x00000:           55 3 15288c 0 8 55 3 15261a 0 1 55 3 15261b 0 0 55 3 15261c 0 1 55 3 15261d 0 32 2

; ---- TABLES ----
T1 (count 0, off 0x0001a): 
T2 (count 0, off 0x0001a): 
T3 (count 0, off 0x0001a): 

; ---- STRINGS ----
  (no inline strings)
```

### CALLBACK_LOST.BIN (176 bytes)
```
file        CALLBACK_LOST.BIN
body        29 dwords (116 bytes)
header      F0=0x3 F1=1 F2=0x1 F3=0x1 F4=1 F5=0x1 F6=0x1c
code        [0x00000 .. 0x0001d)  29 dwords
table T1    off 0x0001d  count 0      tag 0x71  purity 0/0
table T2    off 0x0001d  count 0      tag 0x03  purity 0/0
table T3    off 0x0001d  count 0      tag 0x8f  purity 0/0
strings     1 inline, 1 references

; ---- CODE (chunked by T3 line-index; opcodes not yet decoded) ----
  0x00000:           55 9 0 0 3314 1a7 2 str->0x00019 1a8 5a 9 1 3 62428 0 0 a0 9 1 0 ffffffff 0 18 20c 2   ; 'callback_lost'
  0x00019:  STR  'callback_lost'

; ---- TABLES ----
T1 (count 0, off 0x0001d): 
T2 (count 0, off 0x0001d): 
T3 (count 0, off 0x0001d): 

; ---- STRINGS ----
  [0x00019 @file 0x0000a0] (4dw) 'callback_lost'
```
