export type SetEntry = {
   name: string;
   displayName?: string;
   locks?: string[];
   conflicts?: number[];
   carriedBy?: number[];
   notices?: SetNotice[];
   category?: "legacyset" | "customset" | "persona" | "item" | "basehero" | "prismatic" | "abilityeffect";
   isCustom?: boolean;
   thumbnailUrl?: string | null;
   tag?: string | null;
   styleGroup?: string | null;
   styleLabel?: string | null;
   styleGroupThumbnail?: string | null;
};

export type Hero = {
   id: string;
   name: string;
   displayName?: string;
   attribute?: string;
   abilityNames?: Record<string, string>;
   thumbnail: string;
   sets: SetEntry[];
};

export type HeroSelectionState = {
   set: number | null;
   items: number[];
   base: number | null;
   prismatic: number | null;
   effects?: number[];
};

export type Selections = Record<string, HeroSelectionState>;

export type LatestUpdate = {
   heroId: string;
   heroName: string;
   setIndex: number;
   setThumbnail?: string | null;
   daysAgo: number;
};

export type FilterCategory = "all" | "str" | "agi" | "int" | "universal" | "favorites";

export type TileType = "set" | "persona" | "item" | "base" | "prismatic" | "effect";

export type AlertType = "success" | "warning" | "info";

export type ConfirmItem = { heroName: string; setName: string; thumbnail: string };

export type CooldownState = {
   active: boolean;
   remainingSeconds: number;
   totalSeconds: number;
   dailyUsed: number;
   dailyMax: number;
   isDailyLimit: boolean;
};

export type SetNotice = { type: string; anim?: string; text?: string };
export type DetectionState = { message: string; ready: boolean };
