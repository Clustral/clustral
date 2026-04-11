import { source } from '@/lib/source';
import { DocsLayout } from 'fumadocs-ui/layouts/docs';
import { baseOptions } from '@/lib/layout.shared';
import {
  Rocket,
  Terminal,
  Server,
  Cable,
  GitPullRequest,
} from 'lucide-react';

const tabIcons: Record<string, React.ReactNode> = {
  'Getting Started': <Rocket size={16} />,
  'CLI': <Terminal size={16} />,
  'Control Plane': <Server size={16} />,
  'Agent': <Cable size={16} />,
  'Contributing': <GitPullRequest size={16} />,
};

export default function Layout({ children }: LayoutProps<'/docs'>) {
  return (
    <DocsLayout
      tree={source.getPageTree()}
      {...baseOptions()}
      tabs={{
        transform: (option, node) => ({
          ...option,
          icon: tabIcons[String(node.name)] ?? option.icon,
        }),
      }}
    >
      {children}
    </DocsLayout>
  );
}
