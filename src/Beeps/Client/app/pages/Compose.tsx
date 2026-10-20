// The composer on a page of its own (Compose.cshtml), under the beep it answers. A refused post comes back here with
// its errors and the text kept.
import { useMemo } from 'preact/hooks';
import { putBeeps } from '../beeps.ts';
import { BeepCard } from '../components/BeepCard.tsx';
import { Composer } from '../components/Composer.tsx';
import { FormErrors } from '../components/Forms.tsx';
import { Icon } from '../components/Icon.tsx';
import { t } from '../i18n.ts';
import type { IslandProps } from '../islands.tsx';
import { useLive } from '../page.ts';
import type { BeepProps, ComposerProps, LiveProps } from '../props.ts';

interface ComposeProps {
  live: LiveProps;
  title: string;
  backUrl: string;
  parent?: BeepProps;
  composer?: ComposerProps;
}

export function ComposePage({ props, errors }: IslandProps<ComposeProps>) {
  useLive(props.live);
  useMemo(() => props.parent && putBeeps([props.parent]), [props.parent]);
  return (
    <section class="feed">
      <header class="column-header">
        <a class="column-back" href={props.backUrl} aria-label={t('Nav.Back')}>
          <Icon name="arrow_back" />
        </a>
        <h1 class="column-title">{props.title}</h1>
      </header>
      {/* With a composer, it draws them. */}
      {!props.composer && <FormErrors errors={errors} />}
      {props.parent && <BeepCard id={props.parent.id} />}
      {props.composer && <Composer composer={props.composer} errors={errors} />}
    </section>
  );
}
