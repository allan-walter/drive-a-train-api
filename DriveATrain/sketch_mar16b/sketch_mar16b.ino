#include <Servo.h>

struct ServoConfig {
  int pin;
  int openDeg;
  int closedDeg;
};

Servo servo;

// Define your allowed pins and whether they are reversed
ServoConfig servos[] = {
  {12, 180, 149},   // pin 12, normal
  // {5, true},    // pin 5, reversed
  // {6, false},   // pin 6, normal
};

const int numServos = sizeof(servos) / sizeof(servos[0]);

void loop() {
  if (Serial.available()) {
    String input = Serial.readStringUntil('\n');
    input.trim();

    if (input.length() < 2) return;

    // --- Debug mode: "<pin>:<angle>", e.g. "12:90" ---
    int colonIdx = input.indexOf(':');
    if (colonIdx != -1) {
      int pin = input.substring(0, colonIdx).toInt();
      int angle = input.substring(colonIdx + 1).toInt();

      ServoConfig* cfg = nullptr;
      for (int i = 0; i < numServos; i++) {
        if (servos[i].pin == pin) {
          cfg = &servos[i];
          break;
        }
      }

      if (!cfg) {
        Serial.println("Invalid pin");
        return;
      }

      angle = constrain(angle, 0, 180);

      servo.attach(cfg->pin);
      servo.write(angle);
      Serial.print("Set pin ");
      Serial.print(cfg->pin);
      Serial.print(" to ");
      Serial.println(angle);
      delay(500);
      servo.detach();
      return;
    }

    // --- Normal mode: "<pin>f" or "<pin>c" ---
    char dir = input[input.length() - 1];
    int pin = input.substring(0, input.length() - 1).toInt();

    ServoConfig* cfg = nullptr;
    for (int i = 0; i < numServos; i++) {
      if (servos[i].pin == pin) {
        cfg = &servos[i];
        break;
      }
    }

    if (!cfg) {
      Serial.println("Invalid pin");
      return;
    }

    servo.attach(cfg->pin);

    int angle = (dir == 'f') ? cfg->openDeg : cfg->closedDeg;

    servo.write(angle);
    delay(500);      // wait for servo to move
    servo.detach();  // optional
  }
}

void setup() {
  Serial.begin(115200);
}