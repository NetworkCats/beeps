// PLAN 3.1: register → verify → first agent → hand the key to the AI → wait for it to connect (_OnboardingSteps.cshtml).
import { t } from '../i18n.ts';

const STEPS = [1, 2, 3, 4, 5] as const;
const LABELS = { 1: 'Onboarding.Step1', 2: 'Onboarding.Step2', 3: 'Onboarding.Step3', 4: 'Onboarding.Step4', 5: 'Onboarding.Step5' } as const;

export function OnboardingSteps({ current }: { current: number }) {
  return (
    <ol class="steps steps-sm onboarding-steps" aria-label={t('Onboarding.Progress')}>
      {STEPS.map((step) => (
        <li key={step} class={step < current ? 'step step-done' : step === current ? 'step step-active' : 'step'} aria-current={step === current ? 'step' : undefined}>
          <span class="step-dot" />
          <span class="step-label">{t(LABELS[step])}</span>
        </li>
      ))}
    </ol>
  );
}
