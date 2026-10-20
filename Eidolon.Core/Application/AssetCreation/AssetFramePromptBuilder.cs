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

        public AssetFrame CreateFrame(AssetCollection collection, int frameIndex)
        {
            string label;
            string instruction;
            switch (frameIndex)
            {
                case 0:
                    label = "front";
                    instruction = "Required camera view: FRONT, azimuth exactly 0 degrees relative to the subject's anatomical front"
                        + ", strict orthographic frontal view, camera centered directly in front at subject mid-height"
                        + ", horizontal optical axis, show the front surfaces without exposing a lateral or rear view";
                    break;
                case 1:
                    label = "right";
                    instruction = "Required camera view: RIGHT SIDE, exactly 90 degrees from the front toward the subject's anatomical RIGHT"
                        + ", strict orthographic right lateral profile, camera located exactly on the subject's right at mid-height"
                        + ", SCREEN DIRECTION: the subject's front, nose and forward-facing silhouette point toward SCREEN RIGHT"
                        + ", the subject's own anatomical RIGHT is the near side facing the camera; its anatomical LEFT is the far side"
                        + ", horizontal optical axis, show the actual right-side surfaces, never a mirrored or reused left-side image"
                        + ", rotate only the camera to this exact lateral view instead of copying the reference's screen direction, camera angle or existing multi-view layout"
                        + ", front and rear planes are strictly edge-on; do not expose the frontal planes of the eyes, nose, chest or chest markings"
                        + ", for figures, corresponding shoulders, hips, knees and feet overlap along viewing depth in an exact side projection; preserve the same pose"
                        + ", keep equipment on its original anatomical side and respect near-side occlusion of far-side objects"
                        + ", never pull far-side objects forward or turn them toward the camera to make them visible, no three-quarter or opposite-side substitution";
                    break;
                case 2:
                    label = "left";
                    instruction = "Required camera view: LEFT SIDE, exactly 90 degrees from the front toward the subject's anatomical LEFT"
                        + ", strict orthographic left lateral profile, camera located exactly on the subject's left at mid-height"
                        + ", SCREEN DIRECTION: the subject's front, nose and forward-facing silhouette point toward SCREEN LEFT"
                        + ", the subject's own anatomical LEFT is the near side facing the camera; its anatomical RIGHT is the far side"
                        + ", horizontal optical axis, show the actual left-side surfaces, never a mirrored or reused right-side image"
                        + ", rotate only the camera to this exact lateral view instead of copying the reference's screen direction, camera angle or existing multi-view layout"
                        + ", front and rear planes are strictly edge-on; do not expose the frontal planes of the eyes, nose, chest or chest markings"
                        + ", for figures, corresponding shoulders, hips, knees and feet overlap along viewing depth in an exact side projection; preserve the same pose"
                        + ", keep equipment on its original anatomical side and respect near-side occlusion of far-side objects"
                        + ", never pull far-side objects forward or turn them toward the camera to make them visible, no three-quarter or opposite-side substitution";
                    break;
                case 3:
                    label = "back";
                    instruction = "Required camera view: BACK, azimuth exactly 180 degrees relative to the subject's anatomical front"
                        + ", strict orthographic rear view, camera centered directly behind at subject mid-height"
                        + ", every body part, including the head, must use the same rear camera projection as the torso"
                        + ", horizontal optical axis, show rear surfaces and the backs of clothing and equipment"
                        + ", show only the back of the head and the rear surfaces of head coverings and equipment, never the face, eyes or front visor"
                        + ", do not turn the head or equipment toward the camera or combine a rear torso with a lateral or frontal head"
                        + ", front-facing details are occluded rather than moved onto the rear, no lateral or frontal view";
                    break;
                case 4:
                    label = "top";
                    instruction = "Strict orthographic TOP PLAN VIEW, zenith camera directly above the reference subject looking vertically down parallel to gravity"
                        + ", compact overhead footprint, not a high-angle portrait; ground plane parallel to image plane"
                        + ", only upward-facing surfaces are visible; camera projection and occlusion take priority over displaying identity details"
                        + ", for upright figures, head or headgear crown and shoulder tops dominate; vertical decorations are thin edges, with torso and legs hidden underneath"
                        + ", for upright figures, no face, front headgear panels, frontal chest or waist decoration, or vertical leg segments; do not bow the head, lean the body or lay it down"
                        + ", keep reference shapes, materials, colors, style, pose and anatomical equipment positions; move only the camera, never mirror or move far-side objects into view"
                        + ", front toward image BOTTOM, anatomical RIGHT toward SCREEN LEFT, LEFT toward SCREEN RIGHT, back toward image TOP"
                        + ", fit the visible projected silhouette; one view, plain background, no perspective, text or collage";
                    break;
                default:
                    throw new StudioException(StudioMessageCode.InvalidAssetCollection);
            }
            string prompt = instruction;
            if (frameIndex == 4)
            {
                if (string.IsNullOrWhiteSpace(collection.Prompt) == false)
                {
                    prompt += ", subject appearance only (projection and occlusion take priority): " + collection.Prompt;
                }
            }
            else
            {
                prompt += ", exactly one required view only, no three-quarter, diagonal or mixed camera angles, no perspective exaggeration"
                    + ", move only the camera, keep the reference pose and the subject's facing, joints and equipment fixed in world space"
                    + ", the required view and its occlusion take priority over displaying identity markings; front-facing identity markings being hidden is correct"
                    + ", preserve the exact same subject, visual style, geometry, proportions, costume, markings, colors and materials"
                    + ", anatomical LEFT and RIGHT are fixed on the subject's body; SCREEN LEFT and SCREEN RIGHT refer only to image coordinates"
                    + ", keep every attached or carried object on its original anatomical side with the same orientation"
                    + ", respect natural occlusion from this viewpoint; never swap, mirror, remove or reposition equipment to expose it"
                    + ", centered composition and comparable image scale, keep the complete projected silhouette inside the frame while leaving occluded surfaces hidden"
                    + ", plain background, no labels, text, watermark or multi-view collage"
                    + ", subject description (appearance only): " + collection.Prompt
                    + ", the required camera view and natural occlusion take precedence over any conflicting viewpoint or visibility request in the reference or description";
            }
            if (collection.PixelArt == true)
            {
                prompt += ", pixel art, crisp pixel edges, no anti-aliasing";
            }
            return new AssetFrame
            {
                Number = frameIndex + 1,
                Label = label,
                Seed = collection.Seed,
                Prompt = prompt
            };
        }

    }
}
