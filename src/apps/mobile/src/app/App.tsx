import '../../global.css';

import { Button } from '@teck/ui-native';
import { StatusBar } from 'expo-status-bar';
import { Text, View } from 'react-native';

export const App = () => {
  return (
    <View className="flex-1 items-center justify-center gap-2 bg-background">
      <Text className="text-2xl font-medium text-foreground">@teck/mobile</Text>
      <Button onPress={() => {}}>Primary</Button>
      <Button variant="outline" onPress={() => {}}>
        Outline
      </Button>
      <StatusBar style="auto" />
    </View>
  );
};

export default App;
