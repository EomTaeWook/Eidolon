using System.Globalization;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Application
{
    public class AssetFramePromptBuilder
    {
        public AssetFramePromptBuilder()
        {
        }

        public string Subject(string prompt)
        {
            if (string.IsNullOrWhiteSpace(prompt) == true)
            {
                return "the same single subject shown in the reference image";
            }
            return prompt.Trim();
        }

        public string Action(string prompt, bool hasPoses = false)
        {
            if (string.IsNullOrWhiteSpace(prompt) == true)
            {
                if (hasPoses == true)
                {
                    return "a continuous looping motion following the requested poses";
                }
                return "a seamless walk-in-place cycle at a steady pace";
            }
            return prompt.Trim();
        }

        public AssetFrame CreateFrame(AssetCollection collection, int index, int count, string poseDescription)
        {
            string label = (index + 1).ToString(CultureInfo.InvariantCulture);
            string instruction;
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                instruction = "a consecutive animation exposure, frame " + (index + 1) + " of " + count
                    + ", " + collection.ActionPrompt + ", chronological phase "
                    + ((double)index / count).ToString("P0", CultureInfo.InvariantCulture)
                    + " of one continuous looping motion, equal time between frames";
                if (collection.ActionPrompt == Action(string.Empty))
                {
                    if (string.IsNullOrWhiteSpace(poseDescription) == true)
                    {
                        int phase = index * 8 / count;
                        instruction += ", " + WalkingPose(phase);
                        instruction += ", use natural small opposing arm swings with both elbows below shoulder height"
                            + ", both hands stay near waist height throughout the walk; do not copy any raised-hand gesture from the reference"
                            + ", left and right mean the subject's own anatomical sides, never the viewer's sides";
                    }
                }
                if (string.IsNullOrWhiteSpace(poseDescription) == false)
                {
                    instruction += ", requested pose: " + poseDescription.Trim();
                }
                instruction += ", exactly one full-body animation frame, no sprite sheet or collage"
                    + ", preserve the reference subject's face, silhouette, costume, markings, colors and materials"
                    + ", every worn or carried accessory must remain attached on the same anatomical side in every frame"
                    + ", keep carried shields, weapons and bags visible by using small arm swings; never remove or swap equipment"
                    + ", keep head and body proportions, apparent size, camera distance and viewing angle unchanged"
                    + ", identical framing, fixed bottom-center ground baseline, no camera movement"
                    + ", change only the action pose, physically plausible joint positions and consistent limb lengths"
                    + ", the next frame must continue this motion and the last frame must lead naturally back to the first";
            }
            else
            {
                switch (index)
                {
                    case 0:
                        label = "front";
                        instruction = "Required camera view: FRONT, azimuth 0 degrees relative to the subject's front"
                            + ", camera centered directly in front at subject mid-height, looking horizontally straight at its front";
                        break;
                    case 1:
                        label = "side";
                        instruction = "Required camera view: LEFT SIDE, azimuth 90 degrees from the front toward the subject's left"
                            + ", strict left lateral orthographic profile of the same subject"
                            + ", eye-level camera located exactly on the subject's left, looking horizontally at its side"
                            + ", the front and rear surfaces are edge-on; show the side silhouette"
                            + ", for upright figures the nose points sideways and near and far shoulders overlap in depth"
                            + ", no visible front chest plane, no three-quarter angle";
                        break;
                    case 2:
                        label = "back";
                        instruction = "Required camera view: REAR, azimuth 180 degrees relative to the subject's front"
                            + ", camera centered directly behind at subject mid-height, looking horizontally at its back"
                            + ", show rear surfaces and the back of clothing or equipment, front-facing details must not face the camera";
                        break;
                    case 3:
                        label = "top";
                        instruction = "Required camera view: TOP, elevation 90 degrees, camera directly overhead looking vertically downward"
                            + ", strict orthographic top plan view of the same subject"
                            + ", the optical axis is exactly 90 degrees down, only upward-facing surfaces are visible"
                            + ", depth is along the vertical axis; nearer top surfaces occlude lower surfaces"
                            + ", for upright figures show the crown of the head and tops of shoulders, with the face and front chest hidden"
                            + ", do not tilt the camera or tilt the subject toward it, no diagonal aerial or three-quarter view";
                        break;
                    default:
                        throw new StudioException(StudioMessageCode.InvalidAssetCollection);
                }
                instruction += ", one view only, orthographic or very long-lens projection, no perspective exaggeration"
                    + ", keep the subject in the reference pose, change only the camera viewpoint"
                    + ", preserve the same geometry, proportions, costume, markings, colors, materials and attached objects"
                    + ", centered composition and comparable image scale, no labels or multi-view collage";
            }
            string prompt;
            if (collection.Kind == AssetCreationKind.SpriteAnimation)
            {
                prompt = collection.Prompt + ", " + instruction;
            }
            else
            {
                prompt = instruction + ", subject description (appearance only): " + collection.Prompt
                    + ", use the required camera view even if the reference image or subject description suggests a different direction";
            }
            prompt += ", the exact same subject and visual style as the reference image"
                + ", entire subject visible, plain background, no text or watermark";
            if (collection.PixelArt == true)
            {
                prompt += ", pixel art, crisp pixel edges, no anti-aliasing";
            }
            return new AssetFrame
            {
                Number = index + 1,
                Label = label,
                Seed = collection.Seed,
                Prompt = prompt
            };
        }

        private string WalkingPose(int phase)
        {
            switch (phase)
            {
                case 0:
                    return "left foot forward contacting the ground, right foot behind, right arm forward and left arm back";
                case 1:
                    return "weight settles onto the left foot, left knee bends slightly, right heel lifts, body lowers slightly"
                        + ", right arm still slightly forward and left arm slightly back, both hands lowered near the waist";
                case 2:
                    return "left leg supports the body, right leg passes underneath, both arms pass low beside the torso";
                case 3:
                    return "left supporting leg extends, right knee swings forward, left arm swings forward and right arm back, both hands low";
                case 4:
                    return "right foot forward contacting the ground, left foot behind, left arm forward and right arm back";
                case 5:
                    return "weight settles onto the right foot, right knee bends slightly, left heel lifts, body lowers slightly"
                        + ", left arm still slightly forward and right arm slightly back, both hands lowered near the waist";
                case 6:
                    return "right leg supports the body, left leg passes underneath, both arms pass low beside the torso";
                case 7:
                    return "right supporting leg extends, left knee swings forward, right arm swings forward and left arm back, both hands low"
                        + ", leading into the first left-foot contact";
                default:
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
        }
    }
}
