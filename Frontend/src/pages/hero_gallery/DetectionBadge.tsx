import type { DetectionState } from "./types";
export function DetectionBadge({ detection }: { detection?: DetectionState }) {
 return detection ? <span role="status">{detection.message}</span> : null;
}
