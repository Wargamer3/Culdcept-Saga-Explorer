using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Globalization;
using System.Collections.Generic;

namespace CuldceptSagaExplorer.AssetManager.RSE
{
    public sealed class RseReader
    {
        private readonly string SourceName;
        private RseScene Scene;
        private string Section = "";
        private int Frame;
        private int LineNumber;
        private string Key = "";
        private string Value = "";
        private HashSet<string> SetHeader;
        private HashSet<string> SetParticleHeader;
        private HashSet<int> SetSoundId;
        private List<int> ListSoundFrame;
        private RseParticleGenerator Generator;
        private RseObject Particle;
        private bool FrameDeclared;

        private RseReader(string SourceName)
        {
            this.SourceName = SourceName;
        }

        public static RseScene Parse(string Text, string SourceName = "RSE")
        {
            return new RseReader(SourceName).Read(Text);
        }

        private static Vector3 SetAxis(Vector3 Vector, char Axis, float Value)
        {
            return Axis switch
            {
                'X' => new Vector3(Value, Vector.Y, Vector.Z),
                'Y' => new Vector3(Vector.X, Value, Vector.Z),
                _ => new Vector3(Vector.X, Vector.Y, Value)
            };
        }

        private RseScene Read(string Text)
        {
            if (Text == null)
            {
                throw new ArgumentNullException(nameof(Text));
            }

            Scene = new RseScene();
            SetHeader = new HashSet<string>();
            SetParticleHeader = new HashSet<string>();
            SetSoundId = new HashSet<int>();
            ListSoundFrame = new List<int>();
            Generator = null;
            Particle = null;
            FrameDeclared = false;
            using (StringReader Reader = new StringReader(Text))
            {
                string Line;
                while ((Line = Reader.ReadLine()) != null)
                {
                    ReadLine(Line);
                }
            }

            Validate();
            return Scene;
        }

        private void ReadLine(string Line)
        {
            LineNumber++;
            Line = Line.Trim();
            if (Line.Length == 0 || Line.StartsWith("#") || Line.StartsWith(";"))
            {
                return;
            }

            if (Line.StartsWith("[") && Line.EndsWith("]"))
            {
                Section = Line;
                if (Section == "[KeyData]")
                {
                    FrameDeclared = false;
                }

                if (Section == "[Particle]")
                {
                    Generator = null;
                    SetParticleHeader.Clear();
                }

                if (Section == "[ParticleData]")
                {
                    Particle = null;
                }

                return;
            }

            int Separator = Line.IndexOf('=');
            if (Separator < 1)
            {
                throw Invalid("Expected a key=value assignment.");
            }

            Key = Line.Substring(0, Separator).Trim();
            Value = Line.Substring(Separator + 1).Trim();
            ReadAssignment();
        }

        private void ReadAssignment()
        {
            if (Section == "[Version]" || Section == "[SceneInformation]")
            {
                ReadHeader();
            }
            else if (Section == "[MotionList]")
            {
                ReadMotionList();
            }
            else if (Section == "[EffectList]")
            {
                ReadEffectList();
            }
            else if (Section == "[SoundList]")
            {
                ReadSoundList();
            }
            else if (Section == "[ObjectList]")
            {
                ReadObjectList();
            }
            else if (Section == "[Particle]")
            {
                ReadParticle();
            }
            else if (Section == "[ParticleData]")
            {
                ReadParticleData();
            }
            else if (Section == "[KeyData]" || Section == "[Setup]")
            {
                ReadKeyData();
            }
            else
            {
                throw Unsupported($"Unsupported or non-empty section {Section}: {Key}. Cameras, lights, and general nested-effect scheduling are not yet supported.");
            }
        }

        private void ReadHeader()
        {
            if (!SetHeader.Add(Section + Key))
            {
                throw Invalid($"Duplicate {Key}.");
            }
            int Number = ReadInteger(Value);
            switch ((Section, Key))
            {
                case ("[Version]", "VERSION"):
                    if (Number != 1)
                    {
                        throw Unsupported("Only RSE version 1 is supported.");
                    }

                    break;
                case ("[Version]", "AUTORST"):
                    if (Number != 0 && Number != 1)
                    {
                        throw Invalid("AUTORST must be 0 or 1.");
                    }

                    Scene.AutoTexture = Number == 1;
                    break;
                case ("[Version]", "LOOP"):
                    if (Number != 0 && Number != 1)
                    {
                        throw Invalid("LOOP must be 0 or 1.");
                    }

                    Scene.Loop = Number == 1;
                    break;
                case ("[SceneInformation]", "TotalFrame"):
                    if (Number < 1)
                    {
                        throw Invalid("TotalFrame must be positive.");
                    }

                    Scene.TotalFrames = Number;
                    break;
                case ("[SceneInformation]", "SceneNo"):
                    Scene.SceneNumber = Number;
                    break;
                default:
                    throw Unsupported($"Unsupported {Section} command {Key}.");
            }
        }

        private void ReadMotionList()
        {
            int MotionId = ReadInteger(Key);
            if (MotionId < 0 || !Scene.DicMotion.TryAdd(MotionId, Value))
            {
                throw Invalid("Motion IDs must be unique non-negative integers.");
            }

            if (!string.Equals(Path.GetExtension(Value), ".rsa", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(Value) != Value || Value.Contains(':') || Value.Contains('\\') || Value.Contains('/'))
            {
                throw Unsupported("MotionList requires same-directory RSA references.");
            }
        }

        private void ReadEffectList()
        {
            int EffectId = ReadInteger(Key);
            if (EffectId < 0 || !Scene.DicEffect.TryAdd(EffectId, Value))
            {
                throw Invalid("Effect IDs must be unique non-negative integers.");
            }

            if (!string.Equals(Path.GetExtension(Value), ".RSE", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(Value) != Value || Value.Contains(':') || Value.Contains('\\') || Value.Contains('/'))
            {
                throw Unsupported("EffectList requires same-directory RSE references.");
            }
        }

        private void ReadSoundList()
        {
            int SoundId = ReadInteger(Key);
            if (SoundId < 0 || !SetSoundId.Add(SoundId) || Value.Length == 0)
            {
                throw Invalid("Sound IDs must be unique non-negative integers with a non-empty reference.");
            }
            // Audio is deliberately excluded from visual preview/export; do not resolve files.
        }

        private void ReadObjectList()
        {
            int ObjectId = ReadInteger(Key);
            if (ObjectId < 0 || Scene.DicObject.ContainsKey(ObjectId) || Scene.DicIgnoredModelDeclaration.ContainsKey(ObjectId))
            {
                throw Invalid("Object IDs must be unique non-negative integers.");
            }

            RseObjectKind Kind = Value switch
            {
                "DUMMY_PARTICLE_GENERATOR" => RseObjectKind.ParticleGenerator,
                "DUMMY_PARTICLE" => RseObjectKind.Particle,
                "DUMMY_UVANIM" => RseObjectKind.UvAnimationDummy,
                "DUMMY_EFFECT_GENERATOR" => RseObjectKind.EffectGenerator,
                _ => RseObjectKind.Model
            };
            if (Kind == RseObjectKind.Model
                && string.Equals(Path.GetExtension(Value), ".RAM", StringComparison.OrdinalIgnoreCase)
                && !Value.Contains(".RSM", StringComparison.OrdinalIgnoreCase)
                && !Value.Contains("DUMMY_", StringComparison.Ordinal) && !Value.Contains("Camera", StringComparison.Ordinal)
                && Path.GetFileName(Value) == Value && !Value.Contains(':') && !Value.Contains('\\') && !Value.Contains('/'))
            {
                Scene.DicIgnoredModelDeclaration.Add(ObjectId, Value);
                return;
            }

            if (Kind == RseObjectKind.Model && (!string.Equals(Path.GetExtension(Value), ".rsm", StringComparison.OrdinalIgnoreCase)
                || Path.GetFileName(Value) != Value || Value.Contains(':') || Value.Contains('\\') || Value.Contains('/')))
            {
                throw Unsupported("Only same-directory RSM references and baked particle/generator objects are supported.");
            }

            string FileName = null;
            if (Kind == RseObjectKind.Model)
            {
                FileName = Value;
            }

            Scene.DicObject.Add(ObjectId, new RseObject { Id = ObjectId, Kind = Kind, FileName = FileName });
        }

        private void ReadParticle()
        {
            if (Generator == null || Key == "GENERATOR" && SetParticleHeader.Contains("GENERATOR"))
            {
                Generator = new RseParticleGenerator();
                Scene.ListParticleGenerator.Add(Generator);
                SetParticleHeader.Clear();
            }
            if (!SetParticleHeader.Add(Key))
            {
                throw Invalid($"Duplicate particle {Key}.");
            }

            if (Key == "RSMSTART")
            {
                // The executable's exact-key parser does not recognize this exporter field.
                Generator.IgnoredRsmStart = ReadInteger(Value);
            }
            else if (Key == "PARTICLE")
            {
                string[] Values = ReadValues(2);
                Generator.FirstParticle = ReadInteger(Values[0]);
                Generator.LastParticle = ReadInteger(Values[1]);
            }
            else if (Key == "ANGX" || Key == "ANGY" || Key == "ANGZ")
            {
                string[] Values = ReadValues(2);
                float Angle = ReadFloat(Values[0]);
                float Range = ReadFloat(Values[1]);
                float RandomSpan = Range * 100 * 2;
                if (Range < 0 || !float.IsFinite(RandomSpan) || RandomSpan >= uint.MaxValue || Range > 0 && RandomSpan < 1)
                {
                    throw Unsupported("The particle angle range cannot be represented by the game's integer random modulus.");
                }

                Generator.Angle = SetAxis(Generator.Angle, Key[3], Angle);
                Generator.AngleRange = SetAxis(Generator.AngleRange, Key[3], Range);
            }
            else if (Key == "ROTX" || Key == "ROTY" || Key == "ROTZ")
            {
                string[] Values = ReadValues(2);
                float Velocity = ReadFloat(Values[0]);
                float Range = ReadFloat(Values[1]);
                float RandomSpan = Range * 100 * 2;
                if (Range < 0 || !float.IsFinite(RandomSpan) || RandomSpan >= uint.MaxValue || Range > 0 && RandomSpan < 1)
                {
                    throw Unsupported("The particle spin range cannot be represented by the game's integer random modulus.");
                }

                Generator.SpinVelocity = SetAxis(Generator.SpinVelocity, Key[3], Velocity);
                Generator.SpinVelocityRange = SetAxis(Generator.SpinVelocityRange, Key[3], Range);
            }
            else
            {
                if (Key == "OBJ" && !int.TryParse(Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                {
                    if (!string.Equals(Path.GetExtension(Value), ".rsm", StringComparison.OrdinalIgnoreCase)
                        || Path.GetFileName(Value) != Value || Value.Contains(':') || Value.Contains('\\') || Value.Contains('/'))
                    {
                        throw Unsupported("Particle OBJ filenames must reference a same-directory RSM.");
                    }

                    Generator.ModelFileName = Value;
                    return;
                }
                if (Key != "GENERATOR" && Key != "OBJ" && Key != "SEED" && Key != "START"
                    && Key != "MIDDLE" && Key != "END" && Key != "FADEIN" && Key != "FADEOUT" && Key != "SPIN")
                {
                    throw Unsupported($"Unsupported particle command {Key}.");
                }

                int Number = ReadInteger(Value);
                switch (Key)
                {
                    case "GENERATOR": Generator.ObjectId = Number; break;
                    case "OBJ":
                        if (Number < 0)
                        {
                            throw Invalid("Particle OBJ requires a non-negative object ID.");
                        }

                        Generator.ModelObjectId = Number;
                        break;
                    case "SEED": Generator.Seed = Number; break;
                    case "START": Generator.StartOpacity = ReadPercent(Number); break;
                    case "MIDDLE": Generator.MiddleOpacity = ReadPercent(Number); break;
                    case "END": Generator.EndOpacity = ReadPercent(Number); break;
                    case "FADEIN": Generator.FadeIn = ReadPercent(Number); break;
                    case "FADEOUT": Generator.FadeOut = ReadPercent(Number); break;
                    case "SPIN":
                        if (Number < 0 || Number > 5)
                        {
                            throw Unsupported("Only baked particles with SPIN=0 through SPIN=5 are supported.");
                        }

                        Generator.Spin = Number;
                        break;
                    default: throw Unsupported($"Unsupported particle command {Key}.");
                }
            }
        }

        private void ReadParticleData()
        {
            if (Key == "OBJNO")
            {
                Particle = ReadObject(ReadInteger(Value));
                if (Particle.Kind != RseObjectKind.Particle)
                {
                    throw Invalid("OBJNO must reference a declared particle.");
                }
            }
            else if (Key == "KEY3" || Key == "KEY2")
            {
                if (Particle == null)
                {
                    throw Invalid($"{Key} must follow OBJNO.");
                }

                bool QuaternionKey = Key == "KEY2";
                int ValueCount = 12;
                if (QuaternionKey)
                {
                    ValueCount = 13;
                }

                string[] Values = ReadValues(ValueCount);
                int KeyFrame = ReadInteger(Values[0]);
                float Age = ReadFloat(Values[1]);
                float Lifetime = ReadFloat(Values[2]);
                if (KeyFrame != Particle.ListParticleFrame.Count || Lifetime < 0)
                {
                    throw Invalid("Particle frames must be sequential from zero, with non-negative lifetime.");
                }

                Quaternion? Rotation = null;
                if (QuaternionKey)
                {
                    Rotation = new Quaternion(ReadVector(Values, 6), ReadFloat(Values[9]));
                    if (!float.IsFinite(Rotation.Value.LengthSquared()) || Rotation.Value.LengthSquared() < 1e-12f)
                    {
                        throw Invalid("KEY2 quaternion must have finite nonzero length.");
                    }
                }
                Vector3 EulerRotation = Vector3.Zero;
                int ScaleOffset = 10;
                if (!QuaternionKey)
                {
                    EulerRotation = ReadVector(Values, 6);
                    ScaleOffset = 9;
                }

                Particle.ListParticleFrame.Add(new RseParticleFrame(KeyFrame, Age, Lifetime,
                    ReadVector(Values, 3), EulerRotation, ReadVector(Values, ScaleOffset), Rotation));
            }
            else
            {
                throw Unsupported($"Unsupported particle data {Key}; only baked KEY2/KEY3 tracks are supported.");
            }
        }

        private void ReadKeyData()
        {
            if (Section == "[KeyData]" && Key == "TIME")
            {
                ReadTime();
            }
            else if (Key == "SOUND")
            {
                ReadSoundCue();
            }
            else if (Key == "EFFECT")
            {
                ReadEffectCommand();
            }
            else if (Section == "[KeyData]" && (Key == "POS3" || Key == "SCL3"))
            {
                ReadGeneratorTransform();
            }
            else if (Key == "MOTION")
            {
                ReadMotion();
            }
            else if (Key == "UVANIM")
            {
                ReadUvAnimation();
            }
            else if (Section == "[KeyData]" && Key == "QUAT")
            {
                ReadKeyedQuaternion();
            }
            else if (Section == "[Setup]" && (Key == "POS3" || Key == "SCL3" || Key == "QUAT"))
            {
                ReadSetupTransform();
            }
            else
            {
                ReadOpacity();
            }
        }

        private void ReadTime()
        {
            if (FrameDeclared)
            {
                throw Invalid("Duplicate TIME in a KeyData block.");
            }

            Frame = ReadInteger(Value);
            if (Frame < 0)
            {
                throw Invalid("TIME must be non-negative.");
            }

            FrameDeclared = true;
        }

        private void ReadSoundCue()
        {
            if (ReadInteger(Value) < 0)
            {
                throw Invalid("SOUND must reference a non-negative sound ID.");
            }

            if (Section == "[KeyData]")
            {
                ListSoundFrame.Add(Frame);
            }

            Scene.HasSoundCues = true;
        }

        private void ReadEffectCommand()
        {
            string[] Values = ReadValues(2);
            int TargetId = ReadInteger(Values[0]);
            int EffectId = ReadInteger(Values[1]);
            if (ReadObject(TargetId).Kind != RseObjectKind.EffectGenerator)
            {
                throw Invalid("EFFECT requires an effect-generator target.");
            }

            if (Section != "[KeyData]" || Frame != 0 || EffectId < 0)
            {
                throw Unsupported("Nested effects currently require a single frame-zero start; setup, delayed starts and stops are not yet supported.");
            }

            if (Scene.ListEffectCommand.Any(Command => Command.ObjectId == TargetId))
            {
                throw Unsupported("Repeated nested-effect starts are not yet supported.");
            }

            Scene.ListEffectCommand.Add(new RseEffectCommand(TargetId, EffectId, Frame));
        }

        private void ReadGeneratorTransform()
        {
            string[] Values = ReadValues(4);
            RseObject Object = ReadObject(ReadInteger(Values[0]));
            if (Object.Kind != RseObjectKind.ParticleGenerator)
            {
                throw Unsupported($"Keyed {Key} currently supports baked-particle generators only.");
            }

            SortedDictionary<int, Vector3> Keys = Object.DicGeneratorScale;
            string AxisName = "scale";
            if (Key == "POS3")
            {
                Keys = Object.DicGeneratorPosition;
                AxisName = "position";
            }

            if (!Keys.TryAdd(Frame, ReadVector(Values, 1)))
            {
                throw Invalid($"Duplicate generator {AxisName} key for this frame.");
            }
        }

        private void ReadMotion()
        {
            string[] Values = Value.Split(',');
            if (Values.Length != 2 && Values.Length != 4 && Values.Length != 5)
            {
                throw Invalid("MOTION requires two, four or five values.");
            }
            // The game searches the remaining suffix for NOLOOP/LOOP without consuming this legacy integer.
            if (Values.Length == 5)
            {
                ReadInteger(Values[3]);
            }

            RseObject Object = ReadObject(ReadInteger(Values[0]));
            int MotionId = ReadInteger(Values[1]);
            int Transition = 0;
            if (Values.Length != 2)
            {
                Transition = ReadMotionTransition(Values[2], Object.Id);
            }
            if (MotionId < 0)
            {
                throw Invalid("MOTION requires a non-negative motion ID.");
            }

            string LoopToken = Values[Values.Length - 1].Trim();
            if (Values.Length == 2)
            {
                LoopToken = "LOOP";
            }

            if (!LoopToken.Contains("LOOP", StringComparison.Ordinal))
            {
                Scene.ListRejectedMotionCommand.Add(new RseRejectedMotionCommand(
                    CommandFrame(), Value,
                    "Native MOTION parser rejects a suffix containing neither NOLOOP nor LOOP."));
                return;
            }
            if (Object.Kind == RseObjectKind.Particle || Object.Kind == RseObjectKind.UvAnimationDummy
                || Object.Kind == RseObjectKind.EffectGenerator)
            {
                throw Unsupported("MOTION on particle or UV-animation dummies is not yet supported.");
            }

            if (Transition != 0 || LoopToken != "NOLOOP" && LoopToken != "LOOP")
            {
                throw Unsupported("Only a single MOTION with zero transition and LOOP/NOLOOP is supported.");
            }

            if (Object.MotionId.HasValue)
            {
                throw Invalid("Duplicate MOTION for this object.");
            }

            Object.MotionId = MotionId;
            Object.MotionStartFrame = CommandFrame();
            Object.MotionLoops = LoopToken == "LOOP";
        }

        private void ReadUvAnimation()
        {
            if (Section != "[KeyData]")
            {
                throw Unsupported("Setup UVANIM is not yet supported; use a frame-zero KeyData command.");
            }

            string[] Values = ReadValues(4);
            RseObject Object = ReadObject(ReadInteger(Values[0]));
            int UvSlot = ReadInteger(Values[1]);
            if (UvSlot < 0)
            {
                throw Invalid("UVANIM requires a non-negative material slot.");
            }

            if (Object.Kind != RseObjectKind.Model && Object.Kind != RseObjectKind.UvAnimationDummy)
            {
                throw Unsupported("UVANIM on particle/generator dummies is not yet supported.");
            }

            if (!Object.DicMaterialUv.TryGetValue(UvSlot, out SortedDictionary<int, Vector2> Keys))
            {
                Object.DicMaterialUv.Add(UvSlot, Keys = new SortedDictionary<int, Vector2>());
            }

            if (!Keys.TryAdd(Frame, new Vector2(ReadFloat(Values[2]), ReadFloat(Values[3]))))
            {
                throw Invalid("Duplicate UVANIM key for this frame and material slot.");
            }

            RecordIgnoredDummyCommand(Object);
        }

        private void ReadKeyedQuaternion()
        {
            string[] Values = ReadValues(5);
            RseObject Object = ReadObject(ReadInteger(Values[0]));
            if (Object.Kind == RseObjectKind.Particle)
            {
                throw Unsupported("Keyed QUAT on individual particles is not yet supported.");
            }

            Quaternion Rotation = new Quaternion(ReadVector(Values, 1), ReadFloat(Values[4]));
            if (!float.IsFinite(Rotation.LengthSquared()) || Rotation.LengthSquared() < 1e-12f)
            {
                throw Invalid("QUAT must have a finite nonzero length.");
            }

            if (!Object.DicRotation.TryAdd(Frame, Quaternion.Normalize(Rotation)))
            {
                throw Invalid("Duplicate quaternion key for this frame.");
            }

            RecordIgnoredDummyCommand(Object);
        }

        private void ReadSetupTransform()
        {
            int ValueCount = 4;
            if (Key == "QUAT")
            {
                ValueCount = 5;
            }

            string[] Values = ReadValues(ValueCount);
            RseObject Object = ReadObject(ReadInteger(Values[0]), AllowUnmatchedSetup: true);
            if (Object.Kind == RseObjectKind.Particle)
            {
                throw Unsupported("Setup transforms on individual particles are not yet supported.");
            }

            Vector3 Vector = ReadVector(Values, 1);
            if (Key == "POS3")
            {
                Object.Position = Vector;
            }
            else if (Key == "SCL3")
            {
                Object.Scale = Vector;
            }
            else
            {
                Quaternion Rotation = new Quaternion(Vector, ReadFloat(Values[4]));
                if (!float.IsFinite(Rotation.LengthSquared()) || Rotation.LengthSquared() < 1e-12f)
                {
                    throw Invalid("QUAT must have a finite nonzero length.");
                }

                Object.Rotation = Quaternion.Normalize(Rotation);
            }

            RecordIgnoredDummyCommand(Object);
        }

        private void ReadOpacity()
        {
            if (Key != "MOPA" && Key != "OPACITY")
            {
                throw Unsupported($"Unsupported {Section} command {Key}; visual playback supports OPACITY, MOPA, UVANIM, single LOOP/NOLOOP MOTION, keyed QUAT, baked-generator POS3/SCL3 and setup POS3/QUAT/SCL3. SOUND cues are accepted but not played.");
            }

            string[] ArrayValue = Value.Split(',');
            int ExpectedCount = 2;
            if (Key == "MOPA")
            {
                ExpectedCount = 3;
            }
            if (ArrayValue.Length != ExpectedCount)
            {
                throw Invalid($"{Key} requires {ExpectedCount} integers.");
            }

            int ObjectId = ReadInteger(ArrayValue[0]);
            int Slot = -1;
            if (Key == "MOPA")
            {
                Slot = ReadInteger(ArrayValue[1]);
            }
            int Percent = ReadInteger(ArrayValue[ExpectedCount - 1]);
            if (Slot < -1 || (Key == "MOPA" && Slot < 0))
            {
                throw Invalid("Material slot must be non-negative.");
            }

            RseObject ActiveObject = ReadObject(ObjectId, AllowUnmatchedSetup: Section == "[Setup]");
            if (Percent < 0 || Percent > 100)
            {
                if (ActiveObject.Kind != RseObjectKind.Model || !Scene.DicObject.ContainsKey(ObjectId))
                {
                    throw Invalid("Raw opacity requires a declared model target.");
                }

                Scene.ListRawOpacityCommand.Add(new RseRawOpacityCommand(ObjectId,
                    CommandFrame(), Key, Value));
            }
            if (ActiveObject.Kind != RseObjectKind.Model && ActiveObject.Kind != RseObjectKind.UvAnimationDummy)
            {
                throw Unsupported("Opacity commands on particle/generator dummies are not yet supported.");
            }

            if (Section == "[Setup]")
            {
                if (Slot == -1)
                {
                    ActiveObject.DicSetupOpacity.Clear();
                }

                ActiveObject.DicSetupOpacity[Slot] = Percent / 100f;
            }
            else
            {
                if (Key == "OPACITY")
                {
                    if (!ActiveObject.DicObjectOpacity.TryAdd(Frame, Percent / 100f)
                        && ActiveObject.DicObjectOpacity[Frame] != Percent / 100f)
                    {
                        throw Invalid("Duplicate object opacity key has conflicting values for this frame.");
                    }
                }
                else
                {
                    if (!ActiveObject.DicMaterialOpacity.TryGetValue(Slot, out SortedDictionary<int, float> DicKeys))
                    {
                        ActiveObject.DicMaterialOpacity.Add(Slot, DicKeys = new SortedDictionary<int, float>());
                    }

                    if (!DicKeys.TryAdd(Frame, Percent / 100f) && DicKeys[Frame] != Percent / 100f)
                    {
                        throw Invalid("Duplicate opacity key has conflicting values for this frame and material slot.");
                    }
                }
            }

            RecordIgnoredDummyCommand(ActiveObject);
        }

        private void Validate()
        {
            if (!SetHeader.Contains("[Version]VERSION") || Scene.TotalFrames == 0)
            {
                throw Invalid("VERSION=1 and a positive TotalFrame are required.");
            }
            if (!SetHeader.Contains("[Version]AUTORST"))
            {
                throw Unsupported("An explicit AUTORST value is required; the game's missing-value default is not verified.");
            }
            if (Scene.DicObject.Values.Any(Object => Object.DicMaterialOpacity.Values.Any(DicKeys => DicKeys.Keys.Any(KeyFrame => KeyFrame >= Scene.TotalFrames))))
            {
                throw Invalid("An opacity key lies outside TotalFrame.");
            }
            if (ListSoundFrame.Any(KeyFrame => KeyFrame >= Scene.TotalFrames))
            {
                throw Invalid("A sound cue lies outside TotalFrame.");
            }

            if (Scene.ListRejectedMotionCommand.Any(Command => Command.Frame >= Scene.TotalFrames))
            {
                throw Invalid("A rejected MOTION key lies outside TotalFrame.");
            }

            if (Scene.ListUnmatchedSetupCommand.Any(Command => Scene.DicObject.ContainsKey(Command.ObjectId)))
            {
                throw Unsupported("Setup commands preceding their object declarations are not yet supported.");
            }

            foreach (RseEffectCommand Command in Scene.ListEffectCommand)
            {
                if (!Scene.DicEffect.ContainsKey(Command.EffectId))
                {
                    throw Invalid($"Reference to undeclared effect {Command.EffectId}.");
                }

                RseObject Target = Scene.DicObject[Command.ObjectId];
                if (Target.Position != Vector3.Zero || Target.Rotation != Quaternion.Identity || Target.Scale != Vector3.One
                    || Target.DicRotation.Count > 0)
                {
                    throw Unsupported("Nested effects currently require an identity, unkeyed generator transform.");
                }

                if (Target.DicSetupOpacity.Count > 0 || Target.DicObjectOpacity.Count > 0
                    || Target.DicMaterialOpacity.Count > 0 || Target.DicMaterialUv.Count > 0)
                {
                    throw Unsupported("Nested effect-generator opacity and UV commands are not yet supported.");
                }
            }
            foreach (RseObject Object in Scene.DicObject.Values)
            {
                if (Object.DicObjectOpacity.Keys.Any(KeyFrame => KeyFrame >= Scene.TotalFrames))
                {
                    throw Invalid("An object opacity key lies outside TotalFrame.");
                }

                if (Object.DicObjectOpacity.Count > 0 && Object.DicObjectOpacity.Keys.Min() != 0)
                {
                    throw Unsupported("Timed OPACITY currently requires an initial frame-zero key.");
                }

                if (Object.MotionId.HasValue && !Scene.DicMotion.ContainsKey(Object.MotionId.Value))
                {
                    throw Invalid($"Reference to undeclared motion {Object.MotionId.Value}.");
                }

                if (Object.MotionId.HasValue && Object.MotionStartFrame >= Scene.TotalFrames)
                {
                    throw Invalid("A MOTION key lies outside TotalFrame.");
                }

                if (Object.DicMaterialUv.Values.Any(Keys => Keys.Keys.Any(KeyFrame => KeyFrame >= Scene.TotalFrames)))
                {
                    throw Invalid("A UVANIM key lies outside TotalFrame.");
                }

                if (Object.DicGeneratorPosition.Keys.Any(KeyFrame => KeyFrame >= Scene.TotalFrames))
                {
                    throw Invalid("A generator position key lies outside TotalFrame.");
                }

                if (Object.DicGeneratorScale.Keys.Any(KeyFrame => KeyFrame >= Scene.TotalFrames))
                {
                    throw Invalid("A generator scale key lies outside TotalFrame.");
                }

                if (Object.DicRotation.Keys.Any(KeyFrame => KeyFrame >= Scene.TotalFrames))
                {
                    throw Invalid("A quaternion key lies outside TotalFrame.");
                }

                if (Object.Kind == RseObjectKind.Model && Object.DicRotation.Count > 0 && Object.DicRotation.Keys.Min() != 0)
                {
                    throw Unsupported("Keyed model QUAT currently requires an initial frame-zero key.");
                }
            }
            HashSet<int> SetOwnedParticle = new HashSet<int>();
            HashSet<int> SetGenerator = new HashSet<int>();
            foreach (RseParticleGenerator ActiveGenerator in Scene.ListParticleGenerator)
            {
                if (ActiveGenerator.HasModelReference && ActiveGenerator.ModelFileName == null)
                {
                    if (!Scene.DicObject.TryGetValue(ActiveGenerator.ModelObjectId, out RseObject Source))
                    {
                        ActiveGenerator.IsModelObjectMissing = true;
                    }
                    else if (Source.Kind != RseObjectKind.Model)
                    {
                        throw Invalid("A declared particle source must be an RSM object.");
                    }
                }
                if (ReadObject(ActiveGenerator.ObjectId).Kind != RseObjectKind.ParticleGenerator
                    || !SetGenerator.Add(ActiveGenerator.ObjectId)
                    || ActiveGenerator.FirstParticle < 0 || ActiveGenerator.LastParticle < ActiveGenerator.FirstParticle)
                {
                    throw Invalid("Particle generators require unique generator IDs, an RSM object, and a valid declared particle range.");
                }

                foreach (RseObject ActiveParticle in Scene.DicObject.Values.Where(Object => Object.Kind == RseObjectKind.Particle
                    && Object.Id >= ActiveGenerator.FirstParticle && Object.Id <= ActiveGenerator.LastParticle))
                {
                    if (!SetOwnedParticle.Add(ActiveParticle.Id))
                    {
                        throw Invalid("Particle ranges must not overlap on declared particles.");
                    }

                    if ((ActiveGenerator.Spin == 2 || ActiveGenerator.Spin == 3)
                        && ActiveParticle.ListParticleFrame.Any(Key => Key.QuaternionRotation.HasValue))
                    {
                        throw Unsupported("KEY2 quaternion particles with SPIN=2/3 are not yet supported.");
                    }

                    if (ActiveParticle.ListParticleFrame.Count != Scene.TotalFrames)
                    {
                        throw Unsupported("Baked particles must contain exactly TotalFrame sequential KEY2/KEY3 records.");
                    }
                }
            }
            if (Scene.DicObject.Values.Any(Object => Object.Kind == RseObjectKind.Particle && !SetOwnedParticle.Contains(Object.Id)
                || Object.Kind == RseObjectKind.ParticleGenerator && !SetGenerator.Contains(Object.Id)))
            {
                throw Invalid("Every particle and generator must belong to a complete Particle definition.");
            }
        }

        private int CommandFrame()
        {
            if (Section == "[Setup]")
            {
                return -1;
            }

            return Frame;
        }

        private void RecordIgnoredDummyCommand(RseObject Object)
        {
            if (Object.Kind == RseObjectKind.UvAnimationDummy)
            {
                Scene.ListIgnoredDummyCommand.Add(new RseIgnoredDummyCommand(Object.Id,
                    CommandFrame(), Key, Value));
            }
            else if (!Scene.DicObject.ContainsKey(Object.Id))
            {
                Scene.ListUnmatchedSetupCommand.Add(new RseUnmatchedSetupCommand(Object.Id, -1, Key, Value));
            }
        }

        private int ReadMotionTransition(string Number, int ObjectId)
        {
            if (int.TryParse(Number.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int Strict))
            {
                return Strict;
            }

            int Start = 0;
            while (Start < Number.Length && (Number[Start] == ' ' || Number[Start] == '\t'))
            {
                Start++;
            }

            int End = Start;
            if (End < Number.Length && (Number[End] == '+' || Number[End] == '-'))
            {
                End++;
            }

            int Digits = End;
            while (End < Number.Length && Number[End] >= '0' && Number[End] <= '9')
            {
                End++;
            }

            int Result = 0;
            if (End > Digits && !int.TryParse(Number.Substring(Start, End - Start),
                NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out Result))
            {
                throw Invalid($"MOTION transition integer exceeds the supported range: '{Number}'.");
            }

            Scene.ListNativeMotionTransition.Add(new RseNativeMotionTransition(ObjectId,
                CommandFrame(), Number, Result));
            return Result;
        }

        private RseObject ReadObject(int Id, bool AllowUnmatchedSetup = false)
        {
            if (!Scene.DicObject.TryGetValue(Id, out RseObject Object))
            {
                if (AllowUnmatchedSetup && Id >= 0)
                {
                    return new RseObject { Id = Id, Kind = RseObjectKind.Model };
                }

                throw Invalid($"Reference to undeclared object {Id}.");
            }
            return Object;
        }
        private string[] ReadValues(int Count)
        {
            string[] Values = Value.Split(',');
            if (Values.Length != Count)
            {
                throw Invalid($"{Key} requires {Count} values.");
            }

            return Values;
        }
        private Vector3 ReadVector(string[] Values, int Offset)
        {
            return new Vector3(ReadFloat(Values[Offset]), ReadFloat(Values[Offset + 1]), ReadFloat(Values[Offset + 2]));
        }

        private float ReadFloat(string Number)
        {
            if (!float.TryParse(Number.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float Result) || !float.IsFinite(Result))
            {
                throw Invalid($"Invalid finite number '{Number}'.");
            }

            return Result;
        }
        private int ReadPercent(int Number)
        {
            if (Number < 0 || Number > 100)
            {
                throw Invalid("Particle percentages must be 0-100.");
            }

            return Number;
        }
        private int ReadInteger(string Number)
        {
            if (!int.TryParse(Number.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int Result))
            {
                throw Invalid($"Invalid integer '{Number}'.");
            }
            return Result;
        }
        private InvalidDataException Invalid(string Message)
        {
            return new InvalidDataException($"{SourceName}, line {LineNumber}: {Message}");
        }

        private NotSupportedException Unsupported(string Message)
        {
            return new NotSupportedException($"{SourceName}, line {LineNumber}: {Message}");
        }
    }
}
