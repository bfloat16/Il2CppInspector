        for section in ("moraxRuntimeCaches", "moraxNativeStrings"):
            entries = metadata.get(section, [])
            if entries:
                self._status.update_step("Processing MORAX " + section, len(entries))
            for entry in entries:
                self.define_field_from_json(entry)
                self._status.update_progress()
