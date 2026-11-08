/**
 * The three demo users. Each signs in once with `npm run login -- <persona>`, which saves
 * .auth/<persona>.json. The users themselves are made by hand in Dev (docs/walkthrough-films.md).
 */
export type PersonaName = 'agentA' | 'agentB' | 'supervisor';

export interface Persona {
  name: PersonaName;
  /** Shown in the caption band and spoken in the voice-over. */
  label: string;
  role: string;
  businessUnit: string;
}

export const PERSONAS: Record<PersonaName, Persona> = {
  agentA: { name: 'agentA', label: 'Agent A', role: 'CaseDesk Agent', businessUnit: 'CaseDesk BU A' },
  agentB: { name: 'agentB', label: 'Agent B', role: 'CaseDesk Agent', businessUnit: 'CaseDesk BU B' },
  supervisor: { name: 'supervisor', label: 'Supervisor', role: 'CaseDesk Supervisor', businessUnit: 'root business unit' },
};
