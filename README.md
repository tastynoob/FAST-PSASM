# A simple compiler and asm simulator

- serialize/deserialize support
- fast simulate: at least 200M inst/s
- can be embedded in other programs

# Sample

simple sample
```
a = 0
b = 1
c = 2
while b <= 100
    if b & 1
        a = a + b
    end
    b = b + 1
end
```

compile:
```
mv (0) 0
mv (1) 1
mv (2) 2
j L2_condi
L1_loop:
c& (3) (1) 1
b== (3) 0 L3_ifend
c+ (4) (0) (1)
mv (0) (4)
L3_ifend:
c+ (3) (1) 1
mv (1) (3)
L2_condi:
c<= (3) (1) 100
b!= (3) 0 L1_loop
```


asm simulate result:
a = 2500