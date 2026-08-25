#include <Servo.h>

struct Channel {
  uint8_t inPin, servoPin;
  int off, on;
  Servo sv;
  int pos;
};

Channel ch[] = {
  { 12, 11,  60, 120 },
  // { 3, 10, 50, 130 },
};

void setup() {
  for (auto &c : ch) {
    pinMode(c.inPin, INPUT_PULLUP);
    c.sv.attach(c.servoPin);
    c.pos = c.off;
    c.sv.write(c.pos); 
  }
}

void loop() {
  for (auto &c : ch) {
    int target = (digitalRead(c.inPin) == LOW) ? c.on : c.off;
    if (c.pos != target) {
      c.pos += (target > c.pos) ? 1 : -1;
      c.sv.write(c.pos);
    }
  }
  delay(15);
}