kubectl --context admin@talos-lab get pods -A \
    --as=admin@clustral.local \
    --as-group=system:authenticated \
    --as-group=system:masters