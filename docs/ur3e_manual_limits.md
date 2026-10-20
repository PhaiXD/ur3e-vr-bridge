# UR3e Kinematic & Safety Limits Reference

*Source: Universal Robots UR3e User Manual (`710-943-00_UR3e_User_Manual_en_Global.pdf`)*

---

## 1. Reach & Reference Point
- **Nominal Reach:** **500 mm (19.7 in)** [p. 16, §2.1; p. 33, §5.1].
- **Measurement Reference:** The manual specifies that the robot extends *"500 mm from the base joint"* [p. 33, §5.1]. Geometrically, this spherical boundary is centered at the shoulder/base joint intersection $(0, 0, d_1)$ and corresponds to the **tool output flange center** (link $a_2 + a_3 = 456.75\text{ mm}$ plus wrist link offsets to the flange face) [p. 33, §5.1 diagram].

---

## 2. Joint Ranges
- **Tool Flange (Wrist 3):** **Unlimited rotation** ($\pm360^\circ$ nominal default) [p. 16, §2.1].
- **Base, Shoulder, Elbow, Wrist 1, Wrist 2:** **$\pm360^\circ$** for each joint [p. 16, §2.1].
- **PolyScope Software Safety Range (Factory Presets):** $-363^\circ$ to $+363^\circ$ for all 6 joints (Tolerance: $\pm2^\circ$, soft axis limiting PLd Cat 3) [p. 129, §8.3.1; p. 144, §8.4.6 screenshot].

---

## 3. Maximum Joint & TCP Speeds
- **Wrist Joints (Wrist 1, Wrist 2, Wrist 3):** Max **$360^\circ/\text{s}$** ($6.28\text{ rad/s}$) [p. 16, §2.1].
- **Base, Shoulder, Elbow:** Max **$180^\circ/\text{s}$** ($3.14\text{ rad/s}$) [p. 16, §2.1].
- **PolyScope Joint Speed Safety Cap (Factory Preset):** $191^\circ/\text{s}$ (Tolerance: $-11^\circ/\text{s}$ / $1.15^\circ/\text{s}$) [p. 129, §8.3.1; p. 144, §8.4.6].
- **Max Tool / TCP Speed:** Nominal **$\sim1.0\text{ m/s}$ (39.4 in/s)** [p. 16, §2.1]; configurable safety limit up to **$1500\text{ mm/s}$** [p. 141, §8.4.6].

---

## 4. Default Safety Configuration Limits (Factory Presets)
*From PolyScope Robot Limits & Joint Limits screens [p. 141–144, §8.4.6; p. 129, §8.3.1; p. 226–231, §21]:*

| Safety Parameter | Normal Mode (Default) | Reduced Mode (Default) | Tolerance / Accuracy | Affected Area / Stop Type |
| :--- | :--- | :--- | :--- | :--- |
| **Power** | 300 W | 200 W | 10 W | Mechanical work [p. 129, 141] |
| **Momentum** | 25.0 kg·m/s | 10.0 kg·m/s | 3 kg·m/s | Entire robot [p. 129, 141] |
| **Stopping Time** | 400 ms | 300 ms | 50 ms | Cat 0/1/2 stop [p. 129, 141] |
| **Stopping Distance**| 500 mm | 300 mm | 40 mm | Tool & elbow [p. 129, 141] |
| **Tool (TCP) Speed** | 1500 mm/s | 750 mm/s | 50 mm/s | TCP & tool flange [p. 129, 141] |
| **Tool Force** | 150.0 N | 120.0 N | 25 N | TCP clamp limit [p. 129, 141] |
| **Elbow Speed** | 1500 mm/s | 750 mm/s | 50 mm/s | Elbow joint [p. 129, 141] |
| **Elbow Force** | 150.0 N | 120.0 N | 25 N | Elbow clamp limit [p. 129, 141] |
| **Joint Speed** | 191°/s (180°/360° max) | 191°/s | 1.15°/s | Per-joint [p. 129, 144] |
| **Joint Range** | -363° to +363° | -363° to +363° | 2° / 5° | Per-joint [p. 129, 144] |

*Recovery Mode Fixed Limits (non-customizable):* Joint speed $\le 30^\circ/\text{s}$, Tool speed $\le 250\text{ mm/s}$, Force $\le 100\text{ N}$, Momentum $\le 10\text{ kg}\cdot\text{m/s}$, Power $\le 80\text{ W}$ [p. 132, §8.3.3].

---

## 5. Workspace Guidance & Singularity Zones
- **Central Cylindrical Exclusion Volume:** A vertical cylinder directly above and directly below the robot base must be strictly avoided [p. 33, §5.1; p. 37, §5.3.1].
  - *Warning:* Moving the tool close to this cylindrical volume causes joint 1 (base) and wrist joints to rotate excessively fast even at low Cartesian speeds, resulting in loss of control or protective stops [p. 33, §5.1].
- **Three Core Singularity Regions [p. 37–38, §5.3.1]:**
  1. **Outer Workspace Boundary:** Occurs near full extension ($R \approx 500\text{ mm}$). Joint torque leverage decreases while knee-joint forces spike [p. 37, §5.3.1; p. 129, §8.3.1].
  2. **Inner Workspace Limit:** When tool/wrist passes directly above or below the base Z-axis cylinder.
  3. **Wrist Alignment Singularity:** When Wrist 2 rotates into the same plane as the shoulder, elbow, and Wrist 1.
- **Recommended Operating Space:** Keep trajectories within $0.15\text{ m} \le R \le 0.45\text{ m}$ from the shoulder center, offset from the base central Z-axis [p. 33, 37].

---

## 6. Avoiding Protective Stops (speedL / speedJ Streaming)
- **Stop Triggers:** Exceeding any safety limit (speed, position, momentum, power) immediately trips a **Stop Category 0** (Protective Stop, power cut) [p. 130, §8.3.2].
- **Inverse Kinematics Singularities (speedL):** Cartesians commands near the central cylinder or boundary cause Jacobian matrix degeneracy, commanding near-infinite joint velocities. Always cap joint speeds and filter Cartesian velocities before converting to joint targets [p. 33, 37].
- **Momentum & Acceleration Violations:** Rapid directional changes with heavy payloads/grippers exceed the $25\text{ kg}\cdot\text{m/s}$ momentum or dynamic stand reaction torque ($M_z = 170\text{ Nm}$, $M_{xy} = 220\text{ Nm}$) [p. 35, §5.2; p. 129, §8.3.1]. Slew rate limit all acceleration profiles.
- **Radial Force Spikes:** Stretching out fully creates high radial forces from knee mechanics at low speeds, tripping force sensors [p. 129, §8.3.1].
- **Wrist 3 Cable Wrap:** Disable unrestricted rotation if cables are attached to avoid tension-induced stops [p. 144, §8.4.6].

---

## 7. RECOMMENDED SOFTWARE LIMITS (VR Teleoperation Layer)
*Kinematic parameters: $d_1 = 0.15185$, $a_2 = -0.24355$, $a_3 = -0.2132$, $d_4 = 0.13105$, $d_5 = 0.08535$, $d_6 = 0.0921\text{ m}$. Tool: Robotiq 2F ($L_\text{tool} \approx 0.150\text{ m}$; total tool offset $d_6 + L = 0.2421\text{ m}$). Origin: Shoulder joint $(0, 0, 0.15185\text{ m})$.*

1. **Outer Safety Sphere Radius (Centered at Shoulder Joint):**
   - **Wrist Center (Wrist 1/2 intersection):** Max reach is $|a_2| + |a_3| = 456.75\text{ mm}$ ($475.2\text{ mm}$ 3D). With ~10% margin: **`410 mm` (0.410 m)**.
   - **TCP (Robotiq 2F tip):** Max theoretical collinear reach is $456.75 + 242.1 = 698.85\text{ mm}$ ($\sim700\text{ mm}$). With ~10% margin: **`630 mm` (0.630 m)**.
2. **Inner Safety Sphere Radius (Self-Collision Avoidance):**
   - **Wrist Center:** **`160 mm` (0.160 m)** (prevents forearm colliding against upper arm and wrist housing touching shoulder).
   - **TCP:** **`210 mm` (0.210 m)** (prevents gripper fingers/body from striking base pedestal and shoulder housing).
3. **Forbidden Vertical Cylinder Radius (Around Base Z-Axis):**
   - **`95 mm` (0.095 m)** (base radius is $64\text{ mm}$; $95\text{ mm}$ keeps tool and wrist links outside the singular velocity zone and prevents base collision).
4. **Per-Joint Speed Caps (70–80% of Safety Limits):**
   - **Base, Shoulder, Elbow (Joints 1–3):** Safety limit $180^\circ/\text{s}$ $\rightarrow$ **`130 °/s` ($\approx 2.27\text{ rad/s}$)** (~72% of default).
   - **Wrist 1, Wrist 2, Wrist 3 (Joints 4–6):** Spec $360^\circ/\text{s}$ (conservative preset $191^\circ/\text{s}$) $\rightarrow$ **`180 °/s` ($\approx 3.14\text{ rad/s}$)** for teleoperation stability (or max **`250 °/s`** for 70% of $360^\circ/\text{s}$).
   - **Cartesian TCP Linear Speed Cap:** **`350 mm/s` (0.35 m/s)** (well below the $750\text{ mm/s}$ Reduced / $1500\text{ mm/s}$ Normal limits for safe human-in-the-loop interaction).
