# HapticT3 🔪✋

Haptic integration of Haply's Inverse3 robot with SOFA Framework's liver cutting simulation. Experience realistic tactile feedback while performing electrosurgical procedures in a physics-based virtual environment.

<img width="996" height="505" alt="image" src="https://github.com/user-attachments/assets/6cdd0974-4776-4489-baee-7321db27ce0c" />

## 🎯 Overview

HapticT3 bridges the gap between SOFA's advanced soft tissue physics simulation and haptic interaction, enabling users to **feel** a virtual liver surface and perform cutting operations with realistic force feedback through the Inverse3 device.

**The Integration:**
SOFA provides high-fidelity soft body physics while Unity handles real-time haptic rendering and user interaction. The Inverse3 stylus becomes a virtual electrosurgical tool that responds to surface contact and cutting dynamics.

## ✨ Features

- **Dual-Mode Haptic Interaction**:
  - **Inactive Mode (Green)**: Feel the liver surface with high stiffness feedback
  - **Active Mode (Red)**: Soft tissue cutting with reduced stiffness for realistic incision sensation

- **Intelligent Contact Detection**: Multi-probe system along the laser length for accurate surface tracking

- **Visual Feedback**: Color-coded laser (green/inactive, red/cutting) with dynamic lighting

- **Workspace Calibration**: Button-based view centering to maintain optimal haptic workspace

- **Dynamic Mesh Synchronization**: Real-time collider updates as the liver deforms and is cut

- **Scene Reset**: Restore the liver to its original state without restarting

## 🎮 Controls

### Inverse3 + VerseGrip

| Button | Action |
|--------|--------|
| Button 0 | Center view/calibrate workspace |
| Button 1 | Toggle cutting mode (green ↔ red) |

### Haptic Modes

**Inactive Mode (Green Laser):**
- Stiffness: 250 N/m
- Damping: 2 N·s/m
- **Effect**: Feel the liver surface as a firm, solid object

**Cutting Mode (Red Laser):**
- Stiffness: 80 N/m
- Damping: 1.5 N·s/m
- **Effect**: Soft resistance that yields when force is applied, simulating tissue cutting

## 🛠️ Technologies

- **Unity**: 6000.0.3f1
- **SOFA Framework**: v23.12 
- **Haply for Unity**: v3.2.0 (Inverse3 haptic device)
- **SofaUnity Plugin**: Integration layer between SOFA and Unity
- **Language**: C#

## 📋 Requirements

- Unity 6000.0.3f1 or higher
- SOFA Framework v23.12
- SofaUnity plugin
- Haply for Unity SDK v3.2.0+
- Inverse3 + VerseGrip (Haply Robotics)

## 🚀 Setup

1. **Install SOFA Framework**
   - Download SOFA v23.12 from [sofa-framework.org](https://www.sofa-framework.org/)
   - Follow installation instructions for your platform

2. **Clone the repository**
   ```bash
   git clone https://github.com/Facu-Sch/HapticT3.git
   cd HapticT3
   ```

3. **Open in Unity**
   - Open Unity Hub
   - Add project from disk
   - Select the cloned folder
   - Ensure Unity 6000.0.3f1+ is installed

4. **Install Dependencies**
   - **SofaUnity Plugin**: Install according to SOFA documentation
   - **Haply for Unity**: Download from [Haply Developer Portal](https://www.haply.co/developers)

5. **Connect Hardware**
   - Connect Inverse3 via USB
   - Connect VerseGrip to Inverse3
   - Verify device recognition in Unity Console

6. **Run the Demo**
   - Open scene: `Liver_Interaction_With_Inverse3`
   - Press Play
   - Use Button 0 to calibrate workspace
   - Use Button 1 to toggle between feeling and cutting modes

## 📂 Project Structure

```
Assets/
├── Scripts/
│   ├── SofaLiverToolDriver.cs       # Main haptic integration with multi-probe system
│   ├── HapticToolDriver.cs          # Alternative haptic driver implementation
│   ├── SofaLiverColorCutter.cs      # Visual feedback and cutting activation
│   └── ReloadSceneButton.cs         # Scene reset functionality
├── Scenes/
    └── Liver_Interaction_With_Inverse3.unity  # Main demo scene
```

## 🔧 Key Components

### SofaLiverToolDriver.cs

Advanced haptic controller with distributed contact detection:

**Multi-Probe System:**
- Generates collision spheres along the laser/tool length
- Tracks contact across the entire electrosurgical instrument
- Provides more stable and realistic force feedback than single-point contact

**Haptic Material Model:**
- Separate normal/tangential force components
- Configurable stiffness, damping, and friction per mode
- Force limits to prevent device saturation

### HapticMaterial System

Defines tactile properties for different interaction modes:

```csharp
// Inactive (Green) - Firm surface
stiffness: 250 N/m
normalDamping: 15 N·s/m
tangentialDamping: 2 N·s/m
maxForce: 5N

// Active (Red) - Soft/Cutting
stiffness: 80 N/m
normalDamping: 8 N·s/m
tangentialDamping: 1.5 N·s/m
maxForce: 3N
```

## 🎓 Development Context

This project was developed at the Immersive Technologies Group (FIUNER) as an exploration of SOFA Framework's capabilities for medical simulation combined with haptic interaction. As SOFA is renowned for its accurate soft tissue physics, this integration serves as a foundation for future surgical training applications, particularly those requiring realistic tactile feedback.

The project functions both as a technical demonstration for visitors and as a testbed for evaluating SOFA's integration with haptic devices for our ongoing medical simulation research.

## 🧠 Technical Challenges Solved

### Problem: Coordinate Space Mapping
SOFA and Unity use different coordinate systems and scales.

**Solution:** Implemented delta-based position mapping with configurable scaling and workspace calibration button to recenter the view, allowing comfortable interaction despite workspace constraints.

### Problem: Dynamic Mesh Collision
As the liver is cut in SOFA, the mesh topology changes. Unity's standard mesh colliders don't update automatically.

**Solution:** Implemented real-time mesh collider refresh system that clones and updates collision geometry from SOFA's visual model at configurable intervals.

### Problem: Stable Contact Detection
Single-point contact produces unstable forces when the tool orientation changes or during cutting.

**Solution:** Developed multi-probe system that distributes collision spheres along the tool length, providing more consistent contact detection and smoother force feedback.

## 🗺️ Roadmap

- [ ] Migrate to improved collision detection from DeformT1 project
- [ ] Optimize contact detection performance
- [ ] Add haptic texture variation based on tissue properties

## 🤝 Contributing

Contributions, issues, and feature requests are welcome!

## 🙏 Credits

- **SOFA Framework**: Soft tissue physics simulation
- **Haply Robotics**: Inverse3 haptic device and SDK
- **SofaUnity Plugin**: SOFA-Unity integration layer
- Developed at FIUNER - Immersive Technologies Group

## ⚠️ Known Issues

- Contact detection can feel slightly unstable during rapid cutting motions
- Workspace calibration required at the start
- Currently migrating to improved collision system from DeformT1 project

## 👨‍💻 Author

**Facundo Schneider**
- LinkedIn: [linkedin.com/in/facundo-schneider-a6045631b](https://www.linkedin.com/in/facundo-schneider-a6045631b)
- Email: facundoschneider5@gmail.com
- University: FIUNER - Immersive Technologies Group

---

*Built with Unity, SOFA Framework, and Inverse3 for exploring haptic interaction in surgical simulation.*
